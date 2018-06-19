#! "netcoreapp2.1"
#r "nuget: YamlDotNet.NetCore, 1.0.0"

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

if (Args.Count != 1) {
    Console.WriteLine("Please provide the location of a Jekyll repository as an argument. Example: dotnet script main.csx -- path");
    return;
}

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

const string BaseAddress = "https://hjerpbakk.com";
var preamble = ParsePreamble(postPreamble);
var urls = new List<string>() { 
    BaseAddress, 
    GetUrl("feed.xml"), 
    GetUrl("sitemap.xml"), 
    GetUrl("archive"), 
    GetPostUrl(latestPostPath) 
    };
urls.AddRange(preamble.tags.Select(t => GetTagUrl(t)));

Console.WriteLine(string.Join(Environment.NewLine, urls));

string GetUrl(string webPath) => BaseAddress + "/" + webPath;
string GetTagUrl(string tag) => GetUrl("tag/" + tag);

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

struct Preamble {
    public List<string> tags { get; set; }
}

