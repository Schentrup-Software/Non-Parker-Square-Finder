#define SSE

using NonParkerSquareFinder;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

const string mfilePath = "./mfile.txt";
const ulong totalSearchSpace = long.MaxValue / 10_000_000_000_00;


var sw = new Stopwatch();
sw.Start();

var searchSpace = new List<ulong>();

ulong maxCounter = Scalar.Sqrt(totalSearchSpace);
ulong counter = 3;

while (true)
{
    searchSpace.Add(counter * counter++);

    if (maxCounter <= counter)
    {
        break;
    }
}

File.Delete(mfilePath);
await File.AppendAllLinesAsync(mfilePath, searchSpace.Select(x => x.ToString()));

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

        var tests = new ulong[] { (m + (x + y)), (m - (x + y)), (m + (x - y)), (m - (x - y)) };
        var isGood = true;

        foreach (var test in tests) 
        {
            var sqrtTest = Scalar.Sqrt(test);
            if (isGood && Scalar.Pow<ulong>(sqrtTest, 2) == test)
            {
                continue;
            }

            isGood = false;
        }

        if (isGood)
        {
            Console.WriteLine($"Found it: m = {m}, x = {x}, y = {y}");
        }
    }
}));

await Task.WhenAll(endTasks);
sw.Stop();

Console.WriteLine($"None found. Time {sw.Elapsed.Minutes} min. {sw.Elapsed.Seconds} sec.");
