using System;
using System.Collections.Generic;
using System.Linq;

namespace TurtleBot.Utilities
{
    public static class IEnumerableExtensions
    {
        public static T RandomElement<T>(this IEnumerable<T> enumerable, Random rand)
        {
            var index = rand.Next(0, enumerable.Count());
            return enumerable.ElementAt(index);
        }
    }
}