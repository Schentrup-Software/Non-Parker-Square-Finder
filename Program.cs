using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Program
{
    public class Program
    {

        const string mfilePath = "./mfile.txt";
        const uint totalSearchSpace = int.MaxValue / 100;

        public static async Task Main()
        {
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
                    lastNumber = Math.Pow(counter++, 2);
                }

                File.Delete(mfilePath);
                await File.AppendAllLinesAsync(mfilePath, searchSpace.Prepend(totalSearchSpace).Select(x => x.ToString()));
            }

            Console.WriteLine(searchSpace.Count);

            var newSearchSpace = (await PullFromCache(searchSpace))
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

                    if (
                        Math.Sqrt(m + (x + y)) % 1 == 0 &&
                        Math.Sqrt(m - (x + y)) % 1 == 0 &&
                        Math.Sqrt(m + (x - y)) % 1 == 0 &&
                        Math.Sqrt(m - (x - y)) % 1 == 0)
                    {
                        Console.WriteLine($"Found it: m = {m}, x = {x}, m = {y}");
                        return;
                    }
                }
            }));

            await Task.WhenAll(endTasks);
            sw.Stop();

            Console.WriteLine($"None found. Time {sw.Elapsed.Minutes} min. {sw.Elapsed.Seconds} sec.");
        }

        private static async Task<ConcurrentBag<(uint, uint)>> PullFromCache(ConcurrentBag<uint> searchSpace)
        {
            const string filePath = "./xyfile.txt";

            var xfile = File.Exists(filePath) ? await File.ReadAllLinesAsync(filePath) : Array.Empty<string>();
            var highestMExists = int.TryParse(xfile.FirstOrDefault(), out var highestM);

            var newSearchSpace = highestMExists ? new ConcurrentBag<(uint, uint)>(xfile.Skip(1).Select(x =>
            {
                var split = x.Split(',').Select(y => uint.Parse(y));
                return (split.First(), split.Last());
            })) : new ConcurrentBag<(uint, uint)>();
            
            var searchSpaceTasks = searchSpace
                .Where(m => m > highestM)
                .Select(m => Task.Run(() =>
                {                     
                    for(uint x = 1; x < m; x++)
                    {
                        var mPlusX = m + x;
                        var mPlusXLastDigit = mPlusX % 10;

                        if (mPlusXLastDigit == 2 || mPlusXLastDigit == 3 || mPlusXLastDigit == 7 || mPlusXLastDigit == 8)
                        {
                            continue;
                        }

                        var mMinusX = m - x;
                        var mMinusXLastDigit = mMinusX % 10;

                        if (mMinusXLastDigit == 2 || mMinusXLastDigit == 3 || mMinusXLastDigit == 7 || mMinusXLastDigit == 8)
                        {
                            continue;
                        }

                        if (Math.Sqrt(mPlusX) % 1 == 0 && Math.Sqrt(mMinusX) % 1 == 0)
                        {
                            newSearchSpace.Add((m, x));
                        }
                    }
                }));

            await Task.WhenAll(searchSpaceTasks);

            File.Delete(filePath);
            await File.AppendAllLinesAsync(filePath, newSearchSpace.Select(x => $"{x.Item1},{x.Item2}").Prepend(searchSpace.Max().ToString()));        

            return newSearchSpace;
        }
    }

    public static class Extentions
    {
        public static IEnumerable<(T, T)> GetAllPairs<T>(this IEnumerable<T> enumerable)
        {
            if (enumerable.Count() <= 2)
            {
                yield return (enumerable.First(), enumerable.Last());
            }
            else
            {
                var firstItem = enumerable.FirstOrDefault();
                var newEnumerable = enumerable.Skip(1);

                foreach (var otherValue in newEnumerable)
                {
                    yield return (firstItem, otherValue);
                }

                foreach (var subcallValue in GetAllPairs(newEnumerable))
                {
                    yield return subcallValue;
                }
            }
        }
    }
}

