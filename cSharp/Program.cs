using NonParkerSquareFinder;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

const ulong totalSearchSpace = long.MaxValue;
const int segmentSize = 1 << 20;
const ulong smallTripleLimit = 1 << 22;

var sw = new Stopwatch();
sw.Start();

// Every m is a square, so the search runs over its root c
var maxC = Searcher.Sqrt(totalSearchSpace);
var segmentCount = (long)(maxC / segmentSize) + 1;
var smallTriples = Searcher.SmallTriples(Math.Min(maxC, smallTripleLimit));

Console.WriteLine($"Searching {maxC} squares");

long nextSegment = -1;
long doneSegments = 0;
long pairCount = 0;
long foundCount = 0;

var tasks = Enumerable.Range(0, (int)Math.Min(Environment.ProcessorCount, segmentCount)).Select(_ => Task.Run(() =>
{
    var searcher = new Searcher(smallTriples, maxC / smallTripleLimit, segmentSize);

    while (true)
    {
        var segment = Interlocked.Increment(ref nextSegment);

        if (segment >= segmentCount)
        {
            break;
        }

        var firstC = (ulong)segment * segmentSize;
        var lastC = Math.Min(firstC + segmentSize - 1, maxC);

        foreach (var (m, x, y) in searcher.Search(firstC, lastC))
        {
            Interlocked.Increment(ref foundCount);
            Console.WriteLine($"Found it: m = {m}, x = {x}, y = {y}");
        }

        var done = Interlocked.Increment(ref doneSegments);
        var tenths = done * 10 / segmentCount;

        if (segmentCount >= 10 && tenths > (done - 1) * 10 / segmentCount)
        {
            Console.WriteLine($"{tenths * 10}% after {(int)sw.Elapsed.TotalMinutes} min. {sw.Elapsed.Seconds} sec.");
        }
    }

    Interlocked.Add(ref pairCount, searcher.PairCount);
}));

await Task.WhenAll(tasks);
sw.Stop();

Console.WriteLine($"{pairCount} pairs of m and x");
Console.WriteLine($"{(foundCount == 0 ? "None found" : $"Found {foundCount}")}. Time {(int)sw.Elapsed.TotalMinutes} min. {sw.Elapsed.Seconds} sec.");
