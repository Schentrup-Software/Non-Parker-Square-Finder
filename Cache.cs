using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NonParkerSquareFinder
{
    internal class Cache
    {
        const string filePath = "./xyfile.txt";

        public static async Task<ConcurrentBag<(uint, uint)>> PullFromCache(ConcurrentBag<uint> searchSpace)
        {
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
                    for (uint x = 1; x < m; x++)
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
}
