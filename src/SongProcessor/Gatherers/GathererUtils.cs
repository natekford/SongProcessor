﻿using System.Net;

namespace SongProcessor.Gatherers;

public static class GathererUtils
{
	public static HttpClient DefaultGathererClient { get; } = CreateClient();

	public static FormatException InvalidPropertyProvided(
		this IAnimeGatherer gatherer,
		int id,
		string property,
		Exception e)
		=> throw new FormatException($"Invalid {property} provided by {gatherer.Name} for {id}.", e);

	public static KeyNotFoundException UnableToFind(this IAnimeGatherer gatherer, int id)
		=> throw new KeyNotFoundException($"{id} cannot be found in {gatherer.Name}.");

	private static HttpClient CreateClient()
	{
		var client = new HttpClient(new HttpClientHandler()
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
		client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, default, br");
		client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9"); //Make sure we get English results
		client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
		client.DefaultRequestHeaders.Add("Connection", "keep-alive");
		client.DefaultRequestHeaders.Add("pragma", "no-cache");
		client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
		client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/80.0.3987.163 Safari/537.36");
		return client;
	}
}