using Silk.NET.Maths;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NonParkerSquareFinder
{
    internal class Cache
    {
        const string filePath = "./xyfile.txt";

        public static async Task<ConcurrentQueue<(ulong, ulong)>> PullFromCache(List<ulong> searchSpace)
        {
            var xfile = File.Exists(filePath) ? await File.ReadAllLinesAsync(filePath) : Array.Empty<string>();
            var highestMExists = ulong.TryParse(xfile.FirstOrDefault(), out var highestM);

            var newSearchSpace = highestMExists ? new ConcurrentQueue<(ulong, ulong)>(xfile.Skip(1).Select(x =>
            {
                var split = x.Split(',').Select(y => ulong.Parse(y));
                return (split.First(), split.Last());
            })) : new ConcurrentQueue<(ulong, ulong)>();

            var searchSpaceTasks = searchSpace
                .Where(m => m > highestM)
                .Select(m => Task.Run(() =>
                {
                    var maxX = m / 2;

                    for (ulong x = 1; x < maxX; x++)
                    {
                        var mPlusX = m + x;
                        var mMinusX = m - x;

                        var sqrtMPlusX = Scalar.Sqrt(mPlusX);

                        if (Scalar.Pow<ulong>(sqrtMPlusX, 2) == mPlusX)
                        {
                            continue;
                        }

                        var sqrtMMinusX = Scalar.Sqrt(mMinusX);

                        if (Scalar.Pow<ulong>(sqrtMMinusX, 2) == mMinusX) {
                            newSearchSpace.Enqueue((m, x));
                        }
                    }
                }));

            await Task.WhenAll(searchSpaceTasks);

            File.Delete(filePath);
            await File.AppendAllLinesAsync(filePath, newSearchSpace.Select(x => $"{x.Item1},{x.Item2}").Prepend(searchSpace.Max().ToString()));

            return newSearchSpace;
        }
    }
}
