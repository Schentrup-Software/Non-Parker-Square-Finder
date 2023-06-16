using NonParkerSquareFinder;
using Silk.NET.Maths;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


const string mfilePath = "./mfile.txt";
const uint totalSearchSpace = int.MaxValue / 100;

var sw = new Stopwatch();
sw.Start();

var searchSpace = new ConcurrentBag<uint>();

double lastNumber = 4;
int counter = 3;

var mfile = File.Exists(mfilePath) ? await File.ReadAllLinesAsync(mfilePath) : Array.Empty<string>();
var mtotalParses = int.TryParse(mfile.FirstOrDefault(), out var mtotal);
var mSearchSpaceSame = mtotalParses && mtotal == totalSearchSpace;

if (mSearchSpaceSame)
{
    searchSpace = new ConcurrentBag<uint>(mfile.Skip(1).Select(x => uint.Parse(x)));
}
else
{
    while (lastNumber < totalSearchSpace)
    {
        searchSpace.Add((uint)lastNumber);
        lastNumber = Scalar.Pow(counter++, 2);
    }

    File.Delete(mfilePath);
    await File.AppendAllLinesAsync(mfilePath, searchSpace.Prepend(totalSearchSpace).Select(x => x.ToString()));
}

Console.WriteLine(searchSpace.Count);

var newSearchSpace = (await Cache.PullFromCache(searchSpace))
    .GroupBy(x => x.Item1, x => x.Item2)
    .Where(x => x.Count() > 1);

Console.WriteLine(newSearchSpace.Count());

var endTasks = newSearchSpace.Select(group => Task.Run(() =>
{
    var pairedGroups = group.GetAllPairs();

    foreach(var pairs in pairedGroups)
    {
        var x = pairs.Item1;
        var y = pairs.Item2;
        var m = group.Key;

        if (x + y > m)
        {
            return;
        }
        
        if (
            Scalar.Sqrt(m + (x + y)) > 0 &&
            Scalar.Sqrt(m - (x + y)) > 0 &&
            Scalar.Sqrt(m + (x - y)) > 0 &&
            Scalar.Sqrt(m - (x - y)) > 0
        ) {
            Console.WriteLine($"Found it: m = {m}, x = {x}, y = {y}");
            return;
        }
    }
}));

await Task.WhenAll(endTasks);
sw.Stop();

Console.WriteLine($"None found. Time {sw.Elapsed.Minutes} min. {sw.Elapsed.Seconds} sec.");
