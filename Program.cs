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
        const ulong totalSearchSpace = long.MaxValue / 10_000_000;

        public static async Task Main()
        {
            var sw = new Stopwatch();
            sw.Start();

            var searchSpace = new List<ulong>();

            ulong lastNumber = 4;
            ulong counter = 3;
           
            while (lastNumber < totalSearchSpace)
            {
                searchSpace.Add(lastNumber);
                lastNumber = counter * counter++;
            }

            File.Delete(mfilePath);
            await File.AppendAllLinesAsync(mfilePath, searchSpace.Select(x => x.ToString()));

            Console.WriteLine(searchSpace.Count);

            var newSearchSpace = (await PullFromCache(searchSpace))
                .GroupBy(x => x.Item1, x => x.Item2)
                .Where(x => x.Count() > 1);

            Console.WriteLine(newSearchSpace.Count());

            var endTasks = newSearchSpace.Select(group => Task.Run(() =>
            {
                var pairedGroups = group.GetAllPairs();

                foreach (var pair in pairedGroups)
                {
                    ulong m = group.Key;
                    ulong x;
                    ulong y;

                    if (pair.Item1 < pair.Item2)
                    {
                        y = pair.Item1;
                        x = pair.Item2;
                    }
                    else
                    {
                        x = pair.Item1;
                        y = pair.Item2;
                    }

                    if (
                        m >= (x + y) &&
                        Math.Sqrt(m + (x + y)) % 1 == 0 &&
                        Math.Sqrt(m - (x + y)) % 1 == 0 &&
                        Math.Sqrt(m + (x - y)) % 1 == 0 &&
                        Math.Sqrt(m - (x - y)) % 1 == 0)
                    {
                        Console.WriteLine($"Found it: m = {m}, x = {x}, y = {y}");
                        return;
                    }
                }
            }));

            await Task.WhenAll(endTasks);
            sw.Stop();

            Console.WriteLine($"None found. Time {sw.Elapsed.Minutes} min. {sw.Elapsed.Seconds} sec.");
        }

        private static async Task<List<(ulong, ulong)>> PullFromCache(List<ulong> searchSpace)
        {
            const string filePath = "./xyfile.txt";

            var newSearchSpace = new List<(ulong, ulong)>();

            var searchSpacePairs = searchSpace
                .GetAllPairs();

            foreach (var pair in searchSpacePairs)
            {
                ulong m;
                ulong mPlusX;

                if (pair.Item1 < pair.Item2) 
                {
                    m = pair.Item1;
                    mPlusX = pair.Item2;
                }
                else
                {
                    mPlusX = pair.Item1;
                    m = pair.Item2;
                }

                var x = mPlusX - m;

                if (x > m)
                {
                    continue;
                }

                var mMinusX = m - x;
                var mMinusXLastDigit = mMinusX % 10;

                if (mMinusXLastDigit == 2 || mMinusXLastDigit == 3 || mMinusXLastDigit == 7 || mMinusXLastDigit == 8)
                {
                    continue;
                }

                if (Math.Sqrt(mMinusX) % 1 == 0)
                {
                    newSearchSpace.Add((m, x));
                }
            }

            File.Delete(filePath);
            await File.AppendAllLinesAsync(filePath, newSearchSpace.Select(x => $"{x.Item1},{x.Item2}").Prepend(searchSpace.Max().ToString()));        

            return newSearchSpace;
        }
    }

    public static class Extentions
    {
        public static IEnumerable<(T, T)> GetAllPairs<T>(this IEnumerable<T> enumerable)
        {
            var newEnumerable = enumerable.Skip(1);

            foreach (var nextItem in enumerable)
            {
                foreach (var otherValue in newEnumerable)
                {
                    yield return (nextItem, otherValue);
                }

                newEnumerable = newEnumerable.Skip(1);
            }
        }
    }
}

