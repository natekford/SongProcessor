﻿using FluentAssertions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using SongProcessor.FFmpeg;

namespace SongProcessor.Tests.FFmpeg;

[TestClass]
public sealed class SourceInfoGatherer_Tests : FFmpeg_TestsBase
{
	private const string FAKE_FILE = "DoesNotExist.txt";

	[TestMethod]
	[TestCategory(FFPROBE_CATEGORY)]
	public async Task GetAudioInfo_Test()
	{
		var actual = await Gatherer.GetAudioInfoAsync(VideoInfo.File).ConfigureAwait(false);
		actual.Should().BeEquivalentTo(AudioInfo);
	}

	[TestMethod]
	[TestCategory(FFPROBE_CATEGORY)]
	public async Task GetAudioInfoInvalidFile_Test()
	{
		using var temp = new TempDirectory();
		var file = Path.Combine(temp.Dir, FAKE_FILE);
		await File.WriteAllTextAsync(file, file).ConfigureAwait(false);

		Func<Task> getInfo = () => Gatherer.GetAudioInfoAsync(file);
		(await getInfo.Should()
			.ThrowAsync<SourceInfoGatheringException>()
			.ConfigureAwait(false))
			.WithInnerException<ProgramException>();
	}

	[TestMethod]
	public async Task GetAudioInfoNonExistentFile_Test()
	{
		var result = await Gatherer.GetAudioInfoAsync(FAKE_FILE).ConfigureAwait(false);
		result.Should().BeNull();
	}

	[TestMethod]
	[TestCategory(FFPROBE_CATEGORY)]
	public async Task GetVideoInfo_Test()
	{
		var actual = await Gatherer.GetVideoInfoAsync(VideoInfo.File).ConfigureAwait(false);
		actual.Should().BeEquivalentTo(VideoInfo);
	}

	[TestMethod]
	[TestCategory(FFPROBE_CATEGORY)]
	public async Task GetVideoInfoInvalidFile_Test()
	{
		using var temp = new TempDirectory();
		var file = Path.Combine(temp.Dir, FAKE_FILE);
		await File.WriteAllTextAsync(file, file).ConfigureAwait(false);

		Func<Task> getInfo = () => Gatherer.GetVideoInfoAsync(file);
		(await getInfo.Should()
			.ThrowAsync<SourceInfoGatheringException>()
			.ConfigureAwait(false))
			.WithInnerException<ProgramException>();
	}

	[TestMethod]
	public async Task GetVideoInfoNonExistentFile_Test()
	{
		var result = await Gatherer.GetVideoInfoAsync(FAKE_FILE).ConfigureAwait(false);
		result.Should().BeNull();
	}

	[TestMethod]
	[TestCategory(FFMPEG_CATEGORY)]
	public async Task GetVolumeInfo_Test()
	{
		var actual = await Gatherer.GetVolumeInfoAsync(VideoInfo.File).ConfigureAwait(false);
		actual.Should().BeEquivalentTo(VolumeInfo);
	}

	[TestMethod]
	[TestCategory(FFMPEG_CATEGORY)]
	public async Task GetVolumeInfoInvalidFile_Test()
	{
		using var temp = new TempDirectory();
		var file = Path.Combine(temp.Dir, FAKE_FILE);
		await File.WriteAllTextAsync(file, file).ConfigureAwait(false);

		Func<Task> getInfo = () => Gatherer.GetVolumeInfoAsync(file);
		(await getInfo.Should()
			.ThrowAsync<SourceInfoGatheringException>()
			.ConfigureAwait(false))
			.WithInnerException<ProgramException>();
	}

	[TestMethod]
	public async Task GetVolumeInfoNonExistentFile_Test()
	{
		var result = await Gatherer.GetVolumeInfoAsync(FAKE_FILE).ConfigureAwait(false);
		result.Should().BeNull();
	}
}