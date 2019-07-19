#!/usr/bin/env dotnet-script
#r "nuget: YamlDotNet, 6.1.1"
#r "nuget: Newtonsoft.Json, 12.0.2"
#r "nuget: morelinq, 3.2.0"

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Newtonsoft.Json;
using System.Net.Http;
using MoreLinq;

if (Args.Count < 1 || Args.Count > 2) {
    Console.WriteLine("Please provide the location of a Jekyll repository as an argument. Example: dotnet script main.csx -- path");
    return;
}

var dryRun = Args.Count == 2;
if (dryRun) {
    Console.WriteLine("Starting dry run");
}

Console.WriteLine("Finding cached URLs...");
var postPath = Path.GetFullPath(Path.Combine(Args[0], "_posts"));
var latestPostFrontMatter = Directory
    .EnumerateFileSystemEntries(postPath, "*.md")
    .Select(p => ParseFrontMatter(p))
    .MaxBy(f => f.date > f.last_modified_at ? f.date : f.last_modified_at)
    .First();

const string ConfigFileName = "config.json";
var config = File.Exists(ConfigFileName) 
    ? JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigFileName))
    : new Config("https://hjerpbakk.com");

var sitemapUrl = GetUrl("sitemap.xml");
var urls = new List<string>() { 
    config.BaseAddress, 
    GetUrl("feed.xml"), 
    sitemapUrl, 
    GetUrl("archives/"), 
    GetPostUrl(latestPostFrontMatter.Path) 
    };
urls.AddRange(latestPostFrontMatter.tags.Select(t => GetTagUrl(t)));

Console.WriteLine("Found the following URLs:");
Console.WriteLine(string.Join(Environment.NewLine, urls));

if (dryRun) {
    return;
}

if (string.IsNullOrEmpty(config.CloudflareApiKey) 
    || string.IsNullOrEmpty(config.CloudflareEmail)
    || string.IsNullOrEmpty(config.CloudflareZoneId)) {
    Console.WriteLine("Add config.json if you want to automatically clear the Cloudflare cache");
    return;
}

await ClearCloudflareCache();
await WarmupCache();
await SubmitSitemapToGoogle();

string GetUrl(string webPath) => config.BaseAddress + "/" + webPath;
string GetTagUrl(string tag) => GetUrl("tag/" + tag + "/");

string GetPostUrl(string latestPostPath) {
    var postFileName = Path.GetFileNameWithoutExtension(latestPostPath);
    var nameParts = postFileName.Split('-');

    var year = nameParts[0];
    var month = nameParts[1];
    var day = nameParts[2];
    var name = postFileName.Substring(11);

    var postUrl = new StringBuilder("blog");
    postUrl.Append('/');
    postUrl.Append(year);
    postUrl.Append('/');
    postUrl.Append(month);
    postUrl.Append('/');
    postUrl.Append(day);
    postUrl.Append('/');
    postUrl.Append(name);

    return GetUrl(postUrl.ToString());
}

FrontMatter ParseFrontMatter(string postPath) {
    var frontMatterText = GetFrontMatterFromPost();
    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(new UnderscoredNamingConvention())
        .IgnoreUnmatchedProperties()
        .Build();

    var frontMatter = deserializer.Deserialize<FrontMatter>(frontMatterText);
    frontMatter.Path = postPath;
    return frontMatter;

    string GetFrontMatterFromPost() {
        var fullText = File.ReadAllText(postPath);
        var indexOfFirstLineBreak = fullText.IndexOf('\n');
        var indexOfFrontMatterEnd = fullText.IndexOf("---\n", indexOfFirstLineBreak, StringComparison.InvariantCulture);
        if (indexOfFrontMatterEnd == -1) {
            throw new ArgumentException($"Could not parse front matter for {postPath}", nameof(postPath));
        }

        var frontMatterEnd = indexOfFrontMatterEnd + 3;
        var frontMatterText = fullText.Substring(0, frontMatterEnd).Trim('-');
        return frontMatterText;
    }
}

async Task ClearCloudflareCache() {
    Console.WriteLine("Clearing Cloudflare cache...");
    var cloudflareContent = new CloudflareContent(urls);
    var cloudflareContentString = JsonConvert.SerializeObject(cloudflareContent);
    using (var httpClient = new HttpClient()) {
        httpClient.BaseAddress = new Uri("http://api.cloudflare.com/");

        var request = new HttpRequestMessage(HttpMethod.Delete, "client/v4/zones/" + config.CloudflareZoneId + "/purge_cache");
        request.Content = new StringContent(cloudflareContentString, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Auth-Key", config.CloudflareApiKey);
        request.Headers.Add("X-Auth-Email", config.CloudflareEmail);
        
        var response = await httpClient.SendAsync(request);
        var prettyPrintedResponse = FormatJson(await response.Content.ReadAsStringAsync());
        Console.WriteLine(prettyPrintedResponse);
        Console.WriteLine(response.IsSuccessStatusCode
            ? "Cloudflare cache cleared successfully"
            : "Cloudflare request failed");
    }
}

string FormatJson(string json) {
    dynamic parsedJson = JsonConvert.DeserializeObject(json);
    return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
}

async Task WarmupCache() {
    Console.WriteLine("Warming cache...");
    await Task.Delay(TimeSpan.FromSeconds(30));
    foreach (var url in urls) {
        await VerifyUrl(url);
    }  

    Console.WriteLine("All URLs hit successfully");
}

async Task VerifyUrl(string url) {
    try {
        using (var httpClient = new HttpClient()) {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    } catch (System.Exception) {
        Console.WriteLine("Failed to hit " + url);
        throw;
    }
}

async Task SubmitSitemapToGoogle() {
    Console.WriteLine("Submitting sitemap to Google...");
    var googleUrl = "https://www.google.com/ping?sitemap=" + sitemapUrl;
    await VerifyUrl(googleUrl);
}

struct FrontMatter {
    public DateTime date { get; set; }
    public DateTime? last_modified_at { get; set; }
    public List<string> tags { get; set; }
    public string Path { get; set; }
}

readonly struct Config {
    public Config(string baseAddress) {
        BaseAddress = baseAddress;
        CloudflareApiKey = null;
        CloudflareEmail = null;
        CloudflareZoneId = null;
    }

    [JsonConstructor]
    public Config(string baseAddress, string cloudflareApiKey, string cloudflareEmail, string cloudflareZoneId) {
        BaseAddress = baseAddress;
        CloudflareApiKey = cloudflareApiKey;
        CloudflareEmail = cloudflareEmail;
        CloudflareZoneId = cloudflareZoneId;
    }

    public string BaseAddress { get; }
    public string CloudflareApiKey { get; }
    public string CloudflareEmail { get; }
    public string CloudflareZoneId { get; }
}

readonly struct CloudflareContent {
    public CloudflareContent(List<string> urls) {
        files = urls;
    }

    public List<string> files { get; }
}
