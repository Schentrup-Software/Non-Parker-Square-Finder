# Non-Parker Square Finder

A program to search for a magic square of squares, informally known as a "Non-Parker Square" (http://theparkersquare.com/). This app is .NET 5 console app and can be run using Visual Studio.

## Methodology

We are using the following formula to search for the square:

```
m+x        m-(x+y)       m+y
m-(x-y)    m             m+(x-y)
m-y        m+(x+y)       m-x
```

[source](http://ken.duisenberg.com/potw/archive/arch00/000504sol.html)

It is trivial to see that any choice of unique m, x, and y where m > (x + y) will get you a magic square.

From this, we can figure out that m must be a perfect square.

The app works like this:

1. Pick an upper limit on the search space
1. Calculate every square between 0 and the max search space
1. Calculate each and every pairing of squares between 0 and the upper limit of the seach space
1. Calculate the diffrence between each pairing
    1. The smaller number of the pair will be your m
    1. The larger will be m + (x or y)
    1. The diffrence will be your (x or y)
1. Subtract the (x or y) value from m
1. Check the diffrence to see if it is a square
1. If it is, save the value of m and (x or y) as a pair
1. This creates a list of pairings of possible m and (x or y) values
1. Then group togather all the m values
1. Pair every combination of (x or y) values for each m
1. Check each combination of x, y, and m to see if they work

## Results

Unfortunately, the seach came up empty. The space search was every square smaller than max size of uint in C# (4,294,967,295). The [results folder](./Results) contains 2 files. The mfile.txt is every square between 0 and the max uint size. The xyfile.txt is every pair of numbers where: 

1. one is a square
2. if you add and subtract the higher from the lower of them its is a square
3. the bigger number is smaller than uint
