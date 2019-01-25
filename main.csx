#! "netcoreapp2.2"
#r "nuget: YamlDotNet, 5.3.0"
#r "nuget: Newtonsoft.Json, 12.0.1"

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Newtonsoft.Json;
using System.Net.Http;

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
var latestPostPath = Directory.EnumerateFileSystemEntries(postPath).OrderByDescending(p => p).FirstOrDefault();
if (latestPostPath == null) {
    Console.WriteLine("Could not find a single post at " + postPath);
    return;
}

var postPreamble = GetPreambleFromPost(latestPostPath);
if (postPreamble == null) {
    Console.WriteLine(postPath + " did not contain a valid YML preamble.");
    return;
}

const string ConfigFileName = "config.json";
var config = File.Exists(ConfigFileName) 
    ? JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigFileName))
    : new Config() { BaseAddress = "https://hjerpbakk.com" };

var preamble = ParsePreamble(postPreamble);
var sitemapUrl = GetUrl("sitemap.xml");
var urls = new List<string>() { 
    config.BaseAddress, 
    GetUrl("feed.xml"), 
    sitemapUrl, 
    GetUrl("archive/"), 
    GetPostUrl(latestPostPath) 
    };
urls.AddRange(preamble.tags.Select(t => GetTagUrl(t)));

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

string GetPreambleFromPost(string latestPostPath) {
    var fullText = File.ReadAllText(latestPostPath);
    var indexOfFirstLineBreak = fullText.IndexOf('\n');
    var indexOfPreambleEnd = fullText.IndexOf("---\n", indexOfFirstLineBreak, StringComparison.InvariantCulture);
    if (indexOfPreambleEnd == -1) {
        return null;
    }

    var preambleEnd = indexOfPreambleEnd + 3;
    var preambleText = fullText.Substring(0, preambleEnd).Trim('-');
    return preambleText;
}

Preamble ParsePreamble(string preambleText) {
    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(new UnderscoredNamingConvention())
        .IgnoreUnmatchedProperties()
        .Build();

    return deserializer.Deserialize<Preamble>(preambleText);
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

struct Preamble {
    public List<string> tags { get; set; }
}

struct Config {
    public string BaseAddress { get; set; }
    public string CloudflareApiKey { get; set; }
    public string CloudflareEmail { get; set; }
    public string CloudflareZoneId { get; set; }
}

struct CloudflareContent {
    public CloudflareContent(List<string> urls) {
        files = urls;
    }

    public List<string> files { get; }
}
