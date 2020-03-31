﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AdvorangesUtils;
using AMQSongProcessor.Jobs;
using AMQSongProcessor.Models;

namespace AMQSongProcessor
{
	public sealed class SongProcessor : ISongProcessor
	{
		private const int MP3 = -1;

		private static readonly Resolution[] Resolutions = new[]
		{
			new Resolution(MP3, Status.Mp3),
			new Resolution(480, Status.Res480),
			new Resolution(720, Status.Res720)
		};

		public string FixesFile { get; set; } = "fixes.txt";
		public IProgress<ProcessingData>? Processing { get; set; }
		public IProgress<string>? Warnings { get; set; }

		public IReadOnlyList<ISongJob> CreateJobs(IReadOnlyList<Anime> anime)
		{
			var jobs = new List<ISongJob>();
			foreach (var show in anime)
			{
				if (show.Source == null)
				{
					Warnings?.Report($"Source is null: {show.Name}");
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
						/*Warnings.Report($"Source is smaller than {res.Size}p: {show.Name}");*/
					}
					else
					{
						validResolutions.Add(res);
					}
				}

				foreach (var song in show.UnignoredSongs)
				{
					if (!song.HasTimeStamp)
					{
						Warnings?.Report($"Timestamp is null: {song.Name}");
						continue;
					}

					foreach (var res in validResolutions)
					{
						if (!song.IsMissing(res.Status))
						{
							continue;
						}

						var job = res.IsMp3
							? (SongJob)new Mp3SongJob(song)
							: new VideoSongJob(song, res.Size);
						if (!job.AlreadyExists)
						{
							job.Processing = Processing;
							jobs.Add(job);
						}
					}
				}
			}
			return jobs;
		}

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
				foreach (var song in show.UnignoredSongs)
				{
					counts.GetOrAdd(song.FullName, _ => new List<Anime>()).Add(show);
				}
			}

			var file = Path.Combine(dir, FixesFile);
			using var fs = new FileStream(file, FileMode.Create);
			using var sw = new StreamWriter(fs);

			foreach (var show in anime)
			{
				foreach (var song in show.UnignoredSongs)
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

		public async Task ProcessAsync(IReadOnlyList<ISongJob> jobs, CancellationToken? token = null)
		{
			foreach (var job in jobs)
			{
				token?.ThrowIfCancellationRequested();
				await job.ProcessAsync(token).CAF();
			}
		}

		private readonly struct Resolution
		{
			public bool IsMp3 => Size == MP3;
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