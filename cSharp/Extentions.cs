using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NonParkerSquareFinder
{
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
