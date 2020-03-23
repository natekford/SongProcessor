﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using AdvorangesUtils;

using AMQSongProcessor.Models;

namespace AMQSongProcessor
{
	public sealed class SongProcessor : ISongProcessor
	{
		private const string BITRATE = "bitrate";
		private const int FILE_ALREADY_EXISTS = 183;
		private const int MP3_RESOLUTION = -1;
		private const string SIZE = "size";
		private const string SPEED = "speed";
		private const string TIME = "time";

		private static readonly string FfmpegProgressPattern =
			$@"{SIZE}=\s*(?<{SIZE}>\d*)kB\s*" +
			$@"{TIME}=(?<{TIME}>[0-9\\:\\.]+)\s*" +
			$@"{BITRATE}=\s*(?<{BITRATE}>\d*(\.\d+)?)kbits\/s\s*" +
			$@"{SPEED}=(?<{SPEED}>\d*(\.\d+)?)x";

		private static readonly Regex FfmpegProgressRegex
			= new Regex(FfmpegProgressPattern, RegexOptions.Compiled);

		private static readonly Resolution[] Resolutions = new[]
		{
			new Resolution(MP3_RESOLUTION, Status.Mp3),
			new Resolution(480, Status.Res480),
			new Resolution(720, Status.Res720)
		};

		private static readonly AspectRatio SquareSAR = new AspectRatio(1, 1);

		public string FixesFile { get; set; } = "fixes.txt";
		public IProgress<ProcessingData> Processing { get; set; }
		public IProgress<string> Warnings { get; set; }

		public async Task ExportFixesAsync(string dir, IReadOnlyList<Anime> anime)
		{
			if (anime.Count == 0)
			{
				return;
			}

			static string FormatTimeSpan(TimeSpan ts)
			{
				var format = ts.TotalHours < 1 ? @"mm\:ss" : @"hh\:mm\:ss";
				return ts.ToString(format);
			}

			static string FormatTimestamp(Song song)
			{
				var ts = FormatTimeSpan(song.Start);
				if (song.Episode == null)
				{
					return ts;
				}
				return song.Episode.ToString() + "/" + ts;
			}

			var counts = new ConcurrentDictionary<string, List<Anime>>();
			foreach (var show in anime)
			{
				foreach (var song in show.Songs)
				{
					counts.GetOrAdd(song.FullName, _ => new List<Anime>()).Add(show);
				}
			}

			var file = Path.Combine(dir, FixesFile);
			using var fs = new FileStream(file, FileMode.Create);
			using var sw = new StreamWriter(fs);

			foreach (var show in anime)
			{
				foreach (var song in show.Songs)
				{
					if (song.Status != Status.NotSubmitted)
					{
						continue;
					}

					var sb = new StringBuilder();

					sb.Append("**Anime:** ").AppendLine(show.Name);
					sb.Append("**ANNID:** ").AppendLine(show.Id.ToString());
					sb.Append("**Song Title:** ").AppendLine(song.Name);
					sb.Append("**Artist:** ").AppendLine(song.Artist);
					sb.Append("**Type:** ").AppendLine(song.Type.ToString());
					sb.Append("**Episode/Timestamp:** ").AppendLine(FormatTimestamp(song));
					sb.Append("**Length:** ").AppendLine(FormatTimeSpan(song.Length));

					var matches = counts[song.FullName];
					if (matches.Count > 1)
					{
						var others = matches
							.Where(x => x.Id != show.Id)
							.OrderBy(x => x.Id);

						sb.Append("**Duplicate found in:** ")
							.AppendLine(others.Join(x => x.Id.ToString()));
					}

					await sw.WriteAsync(sb.AppendLine()).CAF();
				}
			}
		}

		public async Task ProcessAsync(IReadOnlyList<Anime> anime)
		{
			foreach (var show in anime)
			{
				if (show.Source == null)
				{
					Warnings.Report($"Source is null: {show.Name}");
					continue;
				}
				else if (!File.Exists(show.GetSourcePath()))
				{
					throw new ArgumentException($"{show.Name} {show.Source} does not exist.", nameof(show.Source));
				}

				var validResolutions = new List<Resolution>(Resolutions.Length);
				foreach (var res in Resolutions)
				{
					if (res.Size > show.VideoInfo?.Height)
					{
						Warnings.Report($"Source is smaller than {res.Size}p: {show.Name}");
					}
					else
					{
						validResolutions.Add(res);
					}
				}

				foreach (var song in show.Songs)
				{
					if (!song.HasTimeStamp)
					{
						Warnings.Report($"Timestamp is null: {song.Name}");
						continue;
					}

					foreach (var res in validResolutions)
					{
						if (!song.IsMissing(res.Status))
						{
							continue;
						}

						var t = res.IsMp3
							? ProcessMp3Async(show, song)
							: ProcessVideoAsync(show, song, res.Size);
						await t.CAF();
					}
				}
			}
		}

		private async Task<int> ProcessAsync(long ticks, string path, string args)
		{
			using var process = Utils.CreateProcess(Utils.FFmpeg, args);

			//ffmpeg always outputs to err
			process.ErrorDataReceived += (s, e) =>
			{
				if (e.Data == null)
				{
					return;
				}

				var match = FfmpegProgressRegex.Match(e.Data);
				if (!match.Success)
				{
					return;
				}

				var size = int.Parse(match.Groups[SIZE].Value);
				var time = TimeSpan.Parse(match.Groups[TIME].Value);
				var bitrate = double.Parse(match.Groups[BITRATE].Value);
				var speed = double.Parse(match.Groups[SPEED].Value);
				var percentage = time.Ticks / (double)ticks;
				var data = new ProcessingData(path, size, time, bitrate, speed, percentage);
				Processing.Report(data);
			};

			return await process.RunAsync(false).CAF();
		}

		private Task<int> ProcessMp3Async(Anime anime, Song song)
		{
			var path = song.GetMp3Path(anime);
			if (File.Exists(path))
			{
				return Task.FromResult(FILE_ALREADY_EXISTS);
			}

			#region Args
			const string ARGS =
				" -f mp3" +
				" -b:a 320k";

			string args;
			if (song.IsClean)
			{
				args =
					$" -ss {song.Start}" + //Starting time
					$" -to {song.End}" + //Ending time
					$" -i \"{anime.GetSourcePath()}\"" + //Video source
					$" -map 0:a:{song.OverrideAudioTrack}" + //Use the first input's audio
					" -vn";
			}
			else
			{
				args =
					$" -to {song.Length}" +
					$" -i \"{anime.GetCleanSongPath(song)}\"";
			}

			if (song.VolumeModifier != null)
			{
				args += $" -filter:a \"volume={song.VolumeModifier}\"";
			}

			args += ARGS;
			args += $" \"{path}\"";
			#endregion Args

			return ProcessAsync(song.Length.Ticks, path, args);
		}

		private Task<int> ProcessVideoAsync(Anime anime, Song song, int resolution)
		{
			var path = song.GetVideoPath(anime, resolution);
			if (File.Exists(path))
			{
				return Task.FromResult(FILE_ALREADY_EXISTS);
			}

			#region Args
			const string ARGS =
				" -sn" + //No subtitles
				" -shortest" +
				" -c:a libopus" + //Set the audio codec to libopus
				" -b:a 320k" + //Set the audio bitrate to 320k
				" -c:v libvpx-vp9 " + //Set the video codec to libvpx-vp9
				" -b:v 0" + //Constant bitrate = 0 so only the variable one is used
				" -crf 20" + //Variable bitrate, 20 should look lossless
				" -pix_fmt yuv420p" + //Set the pixel format to yuv420p
				" -deadline good" +
				" -cpu-used 1" +
				" -tile-columns 6" +
				" -row-mt 1" +
				" -threads 8" +
				" -ac 2";

			var args =
				$" -ss {song.Start}" + //Starting time
				$" -to {song.End}" + //Ending time
				$" -i \"{anime.GetSourcePath()}\""; //Video source

			if (song.IsClean)
			{
				args +=
					$" -map 0:v:{song.OverrideVideoTrack}" + //Use the first input's video
					$" -map 0:a:{song.OverrideAudioTrack}"; //Use the first input's audio
			}
			else
			{
				args +=
					$" -i \"{anime.GetCleanSongPath(song)}\"" + //Audio source
					$" -map 0:v:{song.OverrideVideoTrack}" + //Use the first input's video
					" -map 1:a"; //Use the second input's audio
			}

			args += ARGS; //Add in the constant args, like quality + cpu usage

			var width = -1;
			var videoFilterParts = new List<string>();
			//Resize video if needed
			if (anime.VideoInfo.SAR != SquareSAR)
			{
				videoFilterParts.Add($"setsar={SquareSAR.ToString('/')}");
				videoFilterParts.Add($"setdar={anime.VideoInfo.DAR.ToString('/')}");
				width = (int)(resolution * anime.VideoInfo.DAR.Ratio);
			}
			if (anime.VideoInfo.Height != resolution || width != -1)
			{
				videoFilterParts.Add($"scale={width}:{resolution}");
			}
			if (videoFilterParts.Count > 0)
			{
				args += $" -filter:v \"{videoFilterParts.Join(",")}\"";
			}

			if (song.VolumeModifier != null)
			{
				args += $" -filter:a \"volume={song.VolumeModifier}\"";
			}

			args += $" \"{path}\"";
			#endregion Args

			return ProcessAsync(song.Length.Ticks, path, args);
		}

		private readonly struct Resolution
		{
			public bool IsMp3 => Size == MP3_RESOLUTION;
			public int Size { get; }
			public Status Status { get; }

			public Resolution(int size, Status status)
			{
				Size = size;
				Status = status;
			}
		}
	}
}