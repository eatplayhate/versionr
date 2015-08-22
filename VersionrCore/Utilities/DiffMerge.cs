// Copyright (c) 2006, 2008 Tony Garnock-Jones <tonyg@lshift.net>
// Copyright (c) 2006, 2008 LShift Ltd. <query@lshift.net>
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation files
// (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
// BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
// ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
//
//
// Migration to C# (3.0 / .Net 2.0 for Visual Studio 2008) from 
// Javascript, Copyright (c) 2012 Tao Klerks <tao@klerks.biz>
// 
// This ported code is NOT cleaned up in terms of style, nor tested/optimized for 
//  performance, nor even tested for correctness across all methods - it is an 
//  extremely simplistic minimal-changes conversion/porting. The plan is to clean 
//  it up to be more pleasant to look at an deal with at a later time.
//  To anyone who is familiar with and understands the original terminology of 
//  diff and diff3 concepts, I apologize for my fanciful naming strategy - I has 
//  to come up with object names and haven't yet had a chance to review the 
//  literature.
// Also added a "diff_merge_keepall()" implementation for simplistic 2-way merge.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace Versionr.Utilities
{
    public class Diff
    {
        #region Arbitrarily-named in-between objects

        public class CandidateThing
        {
            public int file1index;
            public int file2index;
            public CandidateThing chain;
        }

        public class CommonRun
        {
            public int File1 { get; set; }
            public int File2 { get; set; }
            public int Length { get; set; }
        }

        public class commonOrDifferentThing
        {
            public List<string> common { get; set; }
            public List<string> file1 { get; set; }
            public List<string> file2 { get; set; }
        }

        public class patchDescriptionThing
        {
            internal patchDescriptionThing() { }

            internal patchDescriptionThing(string[] file, int offset, int length)
            {
                Offset = offset;
                Length = length;
                Chunk = new List<string>(file.SliceJS(offset, offset + length));
            }

            public int Offset { get; set; }
            public int Length { get; set; }
            public List<string> Chunk { get; set; }
        }

        public class patchResult
        {
            public patchDescriptionThing file1 { get; set; }
            public patchDescriptionThing file2 { get; set; }
        }

        public class chunkReference
        {
            public int offset { get; set; }
            public int length { get; set; }
        }

        public class diffSet
        {
            public chunkReference file1 { get; set; }
            public chunkReference file2 { get; set; }
        }

        public enum Side
        {
            Conflict = -1,
            Left = 0,
            Old = 1,
            Right = 2
        }

        public class diff3Set : IComparable<diff3Set>
        {
            public Side side { get; set; }
            public int file1offset { get; set; }
            public int file1length { get; set; }
            public int file2offset { get; set; }
            public int file2length { get; set; }

            public int CompareTo(diff3Set other)
            {
                if (file1offset != other.file1offset)
                    return file1offset.CompareTo(other.file1offset);
                else
                    return side.CompareTo(other.side);
            }
        }

        public class patch3Set
        {
            public Side side { get; set; }
            public int offset { get; set; }
            public int length { get; set; }
            public int conflictOldOffset { get; set; }
            public int conflictOldLength { get; set; }
            public int conflictRightOffset { get; set; }
            public int conflictRightLength { get; set; }
        }

        private class conflictRegion
        {
            public int file1RegionStart { get; set; }
            public int file1RegionEnd { get; set; }
            public int file2RegionStart { get; set; }
            public int file2RegionEnd { get; set; }
        }

        #endregion

        #region Merge Result Objects

        public interface IMergeResultBlock
        {
            // amusingly, I can't figure out anything they have in common.
        }

        public class MergeOKResultBlock : IMergeResultBlock
        {
            public string[] ContentLines { get; set; }
        }

        public class MergeConflictResultBlock : IMergeResultBlock
        {
            public string[] LeftLines { get; set; }
            public int LeftIndex { get; set; }
            public string[] OldLines { get; set; }
            public int OldIndex { get; set; }
            public string[] RightLines { get; set; }
            public int RightIndex { get; set; }
        }

        #endregion

        #region Methods

        public static void PrintDiff(int[,] c, string[] x, string[] y, int i, int j, int bias, List<CommonRun> crList)
        {
            CommonRun last = crList.Count > 0 ? crList[crList.Count - 1] : null;
            while (true)
            {
                if (i > 0 && j > 0 && x[i + bias - 1] == y[j + bias - 1])
                {
                    if (last != null && last.File1 == i && last.File2 == j)
                    {
                        last.File1--;
                        last.File2--;
                        last.Length++;
                    }
                    else
                    {
                        last = new CommonRun() { File1 = i - 1, File2 = j - 1, Length = 1 };
                        crList.Add(last);
                    }
                    j--;
                    i--;
                }
                else if (j > 0 && (i == 0 || c[i, j - 1] >= c[i - 1, j]))
                {
                    j--;
                }
                else if (i > 0 && (j == 0 || c[i, j - 1] < c[i - 1, j]))
                {
                    i--;
                }
                else
                    break;
            }
        }

        public static List<CommonRun> longest_common_subsequence2(string[] file1, string[] file2)
        {
            int startTrim = 0;
            int endTrim = 0;
            for (int i = 0; i < file1.Length && i < file2.Length; i++)
            {
                if (file1[i] == file2[i])
                    startTrim++;
                else
                    break;
            }
            for (int i = file1.Length - 1, j = file2.Length - 1; j > startTrim && i > startTrim; i--, j--)
            {
                if (file1[i] == file2[j])
                    endTrim++;
                else
                    break;
            }

            int[,] matchArray = new int[file1.Length - startTrim - endTrim + 1, file2.Length - startTrim - endTrim + 1];
            for (int i = 1; i < matchArray.GetLength(0); i++)
            {
                for (int j = 1; j < matchArray.GetLength(1); j++)
                {
                    if (file1[i + startTrim - 1] == file2[j + startTrim - 1])
                        matchArray[i, j] = matchArray[i - 1, j - 1] + 1;
                    else
                        matchArray[i, j] = System.Math.Max(matchArray[i, j - 1], matchArray[i - 1, j]);
                }
            }
            List<CommonRun> lcr = new List<CommonRun>();
            PrintDiff(matchArray, file1, file2, matchArray.GetLength(0) - 1, matchArray.GetLength(1) - 1, startTrim, lcr);
            foreach (var x in lcr)
            {
                x.File1 += startTrim;
                x.File2 += startTrim;
            }
            if (startTrim > 0)
            {
                lcr.Add(new CommonRun() { File1 = 0, File2 = 0, Length = startTrim });
            }
            lcr.Reverse();
            if (endTrim > 0)
            {
                lcr.Add(new CommonRun() { File1 = file1.Length - endTrim, File2 = file2.Length - endTrim, Length = endTrim });
            }
            int longestSequence = matchArray[matchArray.GetLength(0) - 1, matchArray.GetLength(1) - 1];

            return lcr;
        }

        class Card
        {
            public Tuple<List<Card>, int> Backreference;
            public int Index;
        }

        public static List<CommonRun> longest_common_subsequence3(string[] file1, string[] file2)
        {
            string[] rf1 = file1;
            string[] rf2 = file2;

            bool trim = false;
            Dictionary<string, int> sequencesInF2 = new Dictionary<string, int>();
            for (int i = 0; i < rf2.Length; i++)
            {
                string key = trim ? rf2[i].Trim() : rf2[i];
                if (sequencesInF2.ContainsKey(key))
                    sequencesInF2[key] = -1;
                else
                    sequencesInF2[key] = i;
            }
            Dictionary<string, int> sequencesInF1 = new Dictionary<string, int>();
            Dictionary<int, int> matchingSequences = new Dictionary<int, int>();
            for (int i = 0; i < rf1.Length; i++)
            {
                string key = trim ? rf1[i].Trim() : rf1[i];
                int index;
                if (sequencesInF1.TryGetValue(key, out index))
                {
                    if (index != -1)
                    {
                        sequencesInF1[key] = -1;
                        matchingSequences.Remove(index);
                    }
                }
                else
                {
                    sequencesInF1[key] = i;
                    int f2index = 0;
                    if (sequencesInF2.TryGetValue(key, out f2index))
                        matchingSequences[i] = f2index;
                }
            }

            var seq = matchingSequences.OrderBy(x => x.Value).ToList();
            List<List<Card>> cardStack = new List<List<Card>>();

            foreach (var x in seq)
            {
                Tuple<List<Card>, int> backref = null;
                bool success = false;
                for (int i = 0; i < cardStack.Count; i++)
                {
                    List<Card> top = cardStack[i];
                    if (top[top.Count - 1].Index > x.Key)
                    {
                        success = true;
                        top.Add(new Card() { Backreference = backref, Index = x.Key });
                        break;
                    }
                    backref = new Tuple<List<Card>, int>(top, top.Count - 1);
                }
                if (!success)
                {
                    if (cardStack.Count > 0)
                        backref = new Tuple<List<Card>, int>(cardStack[cardStack.Count - 1], cardStack[cardStack.Count - 1].Count - 1);
                    cardStack.Add(new List<Card>(new Card[] { new Card() { Backreference = backref, Index = x.Key } }));
                }
            }
            List<int> longestSequence = new List<int>();
            Tuple<List<Card>, int> last = null;
            if (cardStack.Count > 0)
            {
                List<Card> lastPile = cardStack[cardStack.Count - 1];
                last = new Tuple<List<Card>, int>(lastPile, lastPile.Count - 1);
            }
            while (last != null)
            {
                longestSequence.Add(last.Item1[last.Item2].Index);
                last = last.Item1[last.Item2].Backreference;
            }

            longestSequence.Reverse();
            List<CommonRun> lcr = new List<CommonRun>();
            int head0 = 0;
            int head1 = 0;
            foreach (var x in longestSequence)
            {
                int f2Index = matchingSequences[x];
                int c1 = x - head0 + 1;
                int c2 = f2Index - head1 + 1;
                List<CommonRun> subsequence = longest_common_subsequence2(file1.Skip(head0).Take(c1).ToArray(), file2.Skip(head1).Take(c2).ToArray());
                foreach (var y in subsequence)
                {
                    y.File1 += head0;
                    y.File2 += head1;
                }
                if (lcr.Count > 0 && subsequence.Count > 0)
                {
                    var lastMatch = lcr[lcr.Count - 1];
                    int e1 = lastMatch.File1 + lastMatch.Length;
                    int e2 = lastMatch.File2 + lastMatch.Length;
                    if (subsequence[0].File1 == e1 && subsequence[0].File2 == e2)
                    {
                        lastMatch.Length += subsequence[0].Length;
                        subsequence.RemoveAt(0);
                    }
                }
                lcr.AddRange(subsequence);
                head0 += c1;
                head1 += c2;
            }
            if (head0 != file1.Length || head1 != file2.Length)
            {
                List<CommonRun> subsequence = longest_common_subsequence2(file1.Skip(head0).ToArray(), file2.Skip(head1).ToArray());
                foreach (var y in subsequence)
                {
                    y.File1 += head0;
                    y.File2 += head1;
                }
                if (lcr.Count > 0 && subsequence.Count > 0)
                {
                    var lastMatch = lcr[lcr.Count - 1];
                    int e1 = lastMatch.File1 + lastMatch.Length;
                    int e2 = lastMatch.File2 + lastMatch.Length;
                    if (subsequence[0].File1 == e1 && subsequence[0].File2 == e2)
                    {
                        lastMatch.Length += subsequence[0].Length;
                        subsequence.RemoveAt(0);
                    }
                }
                lcr.AddRange(subsequence);
            }

            return lcr;
        }
        public static CandidateThing longest_common_subsequence(string[] file1, string[] file2, out CommonRun cr)
        {
            /* Text diff algorithm following Hunt and McIlroy 1976.
			 * J. W. Hunt and M. D. McIlroy, An algorithm for differential file
			 * comparison, Bell Telephone Laboratories CSTR #41 (1976)
			 * http://www.cs.dartmouth.edu/~doug/
			 *
			 * Expects two arrays of strings.
			 */
            Dictionary<string, List<int>> equivalenceClasses = new Dictionary<string, List<int>>();
            List<int> file2indices;
            List<CandidateThing> candidates = new List<CandidateThing>();

            candidates.Add(new CandidateThing
            {
                file1index = -1,
                file2index = -1,
                chain = null
            });

            for (int j = 0; j < file2.Length; j++)
            {
                string line = file2[j];
                if (equivalenceClasses.ContainsKey(line))
                    equivalenceClasses[line].Add(j);
                else
                    equivalenceClasses.Add(line, new List<int> { j });
            }

            for (int i = 0; i < file1.Length; i++)
            {
                string line = file1[i];
                if (equivalenceClasses.ContainsKey(line))
                    file2indices = equivalenceClasses[line];
                else
                    file2indices = new List<int>();

                int r = 0;
                int s = 0;
                CandidateThing c = candidates[0];

                for (int jX = 0; jX < file2indices.Count; jX++)
                {
                    int j = file2indices[jX];

                    for (s = r; s < candidates.Count; s++)
                    {
                        if ((candidates[s].file2index < j) &&
                            ((s == candidates.Count - 1) ||
                             (candidates[s + 1].file2index > j)))
                            break;
                    }

                    if (s < candidates.Count)
                    {
                        var newCandidate = new CandidateThing
                        {
                            file1index = i,
                            file2index = j,
                            chain = candidates[s]
                        };
                        while (r >= candidates.Count)
                            candidates.Add(null);
                        candidates[r] = c;
                        r = s + 1;
                        c = newCandidate;
                        if (r == candidates.Count)
                        {
                            break; // no point in examining further (j)s
                        }
                    }
                }

                while (r >= candidates.Count)
                    candidates.Add(null);
                candidates[r] = c;
            }

            // At this point, we know the LCS: it's in the reverse of the
            // linked-list through .chain of
            // candidates[candidates.length - 1].
            cr = null;
            return candidates[candidates.Count - 1];
        }

        private static void processCommon(ref commonOrDifferentThing common, List<commonOrDifferentThing> result)
        {
            if (common.common.Count > 0)
            {
                common.common.Reverse();
                result.Add(common);
                common = new commonOrDifferentThing() { common = new List<string>() };
            }
        }

        public static List<commonOrDifferentThing> diff_comm(string[] file1, string[] file2)
        {
            // We apply the LCS to build a "comm"-style picture of the
            // differences between file1 and file2.

            var result = new List<commonOrDifferentThing>();

            int tail1 = file1.Length;
            int tail2 = file2.Length;

            commonOrDifferentThing common = new commonOrDifferentThing
            {
                common = new List<string>()
            };

            CommonRun cr;
            var crList = Diff.longest_common_subsequence2(file1, file2);
            for (var candidate = Diff.longest_common_subsequence(file1, file2, out cr);
                 candidate != null;
                 candidate = candidate.chain)
            {
                commonOrDifferentThing different = new commonOrDifferentThing
                {
                    file1 = new List<string>(),
                    file2 = new List<string>()
                };

                while (--tail1 > candidate.file1index)
                    different.file1.Add(file1[tail1]);

                while (--tail2 > candidate.file2index)
                    different.file2.Add(file2[tail2]);

                if (different.file1.Count > 0 || different.file2.Count > 0)
                {
                    processCommon(ref common, result);
                    different.file1.Reverse();
                    different.file2.Reverse();
                    result.Add(different);
                }

                if (tail1 >= 0)
                    common.common.Add(file1[tail1]);
            }

            processCommon(ref common, result);

            result.Reverse();
            return result;
        }

        public static List<commonOrDifferentThing> diff_comm2(string[] file1, string[] file2, bool fancy)
        {
            // We apply the LCS to build a "comm"-style picture of the
            // differences between file1 and file2.

            var result = new List<commonOrDifferentThing>();

            int tail1 = file1.Length;
            int tail2 = file2.Length;

            commonOrDifferentThing common = new commonOrDifferentThing
            {
                common = new List<string>()
            };

            CommonRun cr;
            List<CommonRun> crList = null;
            if (fancy)
                crList = Diff.longest_common_subsequence3(file1, file2);
            else
                crList = Diff.longest_common_subsequence2(file1, file2);
            int head0 = 0;
            int head1 = 0;
            foreach (var x in crList)
            {
                if (x.File1 != head0 || x.File2 != head1)
                {
                    commonOrDifferentThing difference = null;
                    bool newDifference = false;
                    if (result.Count > 0 && result[result.Count - 1].common == null)
                        difference = result[result.Count - 1];
                    else
                    {
                        difference = new commonOrDifferentThing()
                        {
                            file1 = new List<string>(),
                            file2 = new List<string>()
                        };
                        newDifference = true;
                    }
                    if (x.File1 != head0)
                    {
                        for (; head0 < x.File1; head0++)
                            difference.file1.Add(file1[head0]);
                    }
                    if (x.File2 != head1)
                    {
                        for (; head1 < x.File2; head1++)
                            difference.file2.Add(file2[head1]);
                    }
                    if (newDifference)
                        result.Add(difference);
                }
                commonOrDifferentThing cc = null;
                bool newItem = false;
                if (result.Count > 0 && result[result.Count - 1].common != null)
                    cc = result[result.Count - 1];
                else
                {
                    cc = new commonOrDifferentThing()
                    {
                        common = new List<string>()
                    };
                    newItem = true;
                }
                for (int i = 0; i < x.Length; i++)
                    cc.common.Add(file1[i + head0]);
                head0 += x.Length;
                head1 += x.Length;
                if (newItem)
                    result.Add(cc);
            }

			if (file1.Length != head0 || file2.Length != head1)
			{
				commonOrDifferentThing difference = new commonOrDifferentThing()
				{
					file1 = new List<string>(),
					file2 = new List<string>()
				};
				if (file1.Length != head0)
				{
					for (; head0 < file1.Length; head0++)
						difference.file1.Add(file1[head0]);
				}
				if (file2.Length != head1)
				{
					for (; head1 < file2.Length; head1++)
						difference.file2.Add(file2[head1]);
				}
				result.Add(difference);
			}
			//processCommon(ref common, result);

			//result.Reverse();
			return result;
        }

        public static List<patchResult> diff_patch(string[] file1, string[] file2)
        {
            // We apply the LCD to build a JSON representation of a
            // diff(1)-style patch.

            var result = new List<patchResult>();
            var tail1 = file1.Length;
            var tail2 = file2.Length;

            CommonRun cr;
            for (var candidate = Diff.longest_common_subsequence(file1, file2, out cr);
                 candidate != null;
                 candidate = candidate.chain)
            {
                var mismatchLength1 = tail1 - candidate.file1index - 1;
                var mismatchLength2 = tail2 - candidate.file2index - 1;
                tail1 = candidate.file1index;
                tail2 = candidate.file2index;

                if (mismatchLength1 > 0 || mismatchLength2 > 0)
                {
                    patchResult thisResult = new patchResult
                    {
                        file1 = new patchDescriptionThing(file1,
                                                         candidate.file1index + 1,
                                                         mismatchLength1),
                        file2 = new patchDescriptionThing(file2,
                                                         candidate.file2index + 1,
                                                         mismatchLength2)
                    };
                    result.Add(thisResult);
                }
            }

            result.Reverse();
            return result;
        }

        public static List<patchResult> strip_patch(List<patchResult> patch)
        {
            // Takes the output of Diff.diff_patch(), and removes
            // information from it. It can still be used by patch(),
            // below, but can no longer be inverted.
            var newpatch = new List<patchResult>();
            for (var i = 0; i < patch.Count; i++)
            {
                var chunk = patch[i];
                newpatch.Add(new patchResult
                {
                    file1 = new patchDescriptionThing
                    {
                        Offset = chunk.file1.Offset,
                        Length = chunk.file1.Length
                    },
                    file2 = new patchDescriptionThing
                    {
                        Chunk = chunk.file1.Chunk
                    }
                });
            }
            return newpatch;
        }

        public static void invert_patch(List<patchResult> patch)
        {
            // Takes the output of Diff.diff_patch(), and inverts the
            // sense of it, so that it can be applied to file2 to give
            // file1 rather than the other way around.
            for (var i = 0; i < patch.Count; i++)
            {
                var chunk = patch[i];
                var tmp = chunk.file1;
                chunk.file1 = chunk.file2;
                chunk.file2 = tmp;
            }
        }

        private static void copyCommon(int targetOffset, ref int commonOffset, string[] file, List<string> result)
        {
            while (commonOffset < targetOffset)
            {
                result.Add(file[commonOffset]);
                commonOffset++;
            }
        }

        public static List<string> patch(string[] file, List<patchResult> patch)
        {
            // Applies a patch to a file.
            //
            // Given file1 and file2, Diff.patch(file1, Diff.diff_patch(file1, file2)) should give file2.

            var result = new List<string>();
            var commonOffset = 0;

            for (var chunkIndex = 0; chunkIndex < patch.Count; chunkIndex++)
            {
                var chunk = patch[chunkIndex];
                copyCommon(chunk.file1.Offset, ref commonOffset, file, result);

                for (var lineIndex = 0; lineIndex < chunk.file2.Chunk.Count; lineIndex++)
                    result.Add(chunk.file2.Chunk[lineIndex]);

                commonOffset += chunk.file1.Length;
            }

            copyCommon(file.Length, ref commonOffset, file, result);
            return result;
        }

        public static List<string> diff_merge_keepall(string[] file1, string[] file2)
        {
            // Non-destructively merges two files.
            //
            // This is NOT a three-way merge - content will often be DUPLICATED by this process, eg
            // when starting from the same file some content was moved around on one of the copies.
            // 
            // To handle typical "common ancestor" situations and avoid incorrect duplication of 
            // content, use diff3_merge instead.
            // 
            // This method's behaviour is similar to gnu diff's "if-then-else" (-D) format, but 
            // without the if/then/else lines!
            //

            var result = new List<string>();
            var file1CompletedToOffset = 0;
            var diffPatches = diff_patch(file1, file2);

            for (var chunkIndex = 0; chunkIndex < diffPatches.Count; chunkIndex++)
            {
                var chunk = diffPatches[chunkIndex];
                if (chunk.file2.Length > 0)
                {
                    //copy any not-yet-copied portion of file1 to the end of this patch entry
                    result.AddRange(file1.SliceJS(file1CompletedToOffset, chunk.file1.Offset + chunk.file1.Length));
                    file1CompletedToOffset = chunk.file1.Offset + chunk.file1.Length;

                    //copy the file2 portion of this patch entry
                    result.AddRange(chunk.file2.Chunk);
                }
            }
            //copy any not-yet-copied portion of file1 to the end of the file
            result.AddRange(file1.SliceJS(file1CompletedToOffset, file1.Length));

            return result;
        }

        public static List<diffSet> diff_indices(string[] file1, string[] file2)
        {
            // We apply the LCS to give a simple representation of the
            // offsets and lengths of mismatched chunks in the input
            // files. This is used by diff3_merge_indices below.

            var result = new List<diffSet>();
            var tail1 = file1.Length;
            var tail2 = file2.Length;

            CommonRun cr;
            for (var candidate = Diff.longest_common_subsequence(file1, file2, out cr);
                 candidate != null;
                 candidate = candidate.chain)
            {
                var mismatchLength1 = tail1 - candidate.file1index - 1;
                var mismatchLength2 = tail2 - candidate.file2index - 1;
                tail1 = candidate.file1index;
                tail2 = candidate.file2index;

                if (mismatchLength1 > 0 || mismatchLength2 > 0)
                {
                    result.Add(new diffSet
                    {
                        file1 = new chunkReference
                        {
                            offset = tail1 + 1,
                            length = mismatchLength1
                        },
                        file2 = new chunkReference
                        {
                            offset = tail2 + 1,
                            length = mismatchLength2
                        }
                    });
                }
            }

            result.Reverse();
            return result;
        }

        private static void addHunk(diffSet h, Side side, List<diff3Set> hunks)
        {
            hunks.Add(new diff3Set
            {
                side = side,
                file1offset = h.file1.offset,
                file1length = h.file1.length,
                file2offset = h.file2.offset,
                file2length = h.file2.length
            });
        }

        private static void copyCommon2(int targetOffset, ref int commonOffset, List<patch3Set> result)
        {
            if (targetOffset > commonOffset)
            {
                result.Add(new patch3Set
                {
                    side = Side.Old,
                    offset = commonOffset,
                    length = targetOffset - commonOffset
                });
            }
        }

        public static List<patch3Set> diff3_merge_indices(string[] a, string[] o, string[] b)
        {
            // Given three files, A, O, and B, where both A and B are
            // independently derived from O, returns a fairly complicated
            // internal representation of merge decisions it's taken. The
            // interested reader may wish to consult
            //
            // Sanjeev Khanna, Keshav Kunal, and Benjamin C. Pierce. "A
            // Formal Investigation of Diff3." In Arvind and Prasad,
            // editors, Foundations of Software Technology and Theoretical
            // Computer Science (FSTTCS), December 2007.
            //
            // (http://www.cis.upenn.edu/~bcpierce/papers/diff3-short.pdf)

            var m1 = Diff.diff_indices(o, a);
            var m2 = Diff.diff_indices(o, b);

            var hunks = new List<diff3Set>();

            for (int i = 0; i < m1.Count; i++) { addHunk(m1[i], Side.Left, hunks); }
            for (int i = 0; i < m2.Count; i++) { addHunk(m2[i], Side.Right, hunks); }
            hunks.Sort();

            var result = new List<patch3Set>();
            var commonOffset = 0;

            for (var hunkIndex = 0; hunkIndex < hunks.Count; hunkIndex++)
            {
                var firstHunkIndex = hunkIndex;
                var hunk = hunks[hunkIndex];
                var regionLhs = hunk.file1offset;
                var regionRhs = regionLhs + hunk.file1length;

                while (hunkIndex < hunks.Count - 1)
                {
                    var maybeOverlapping = hunks[hunkIndex + 1];
                    var maybeLhs = maybeOverlapping.file1offset;
                    if (maybeLhs > regionRhs)
                        break;

                    regionRhs = Math.Max(regionRhs, maybeLhs + maybeOverlapping.file1length);
                    hunkIndex++;
                }

                copyCommon2(regionLhs, ref commonOffset, result);
                if (firstHunkIndex == hunkIndex)
                {
                    // The "overlap" was only one hunk long, meaning that
                    // there's no conflict here. Either a and o were the
                    // same, or b and o were the same.
                    if (hunk.file2length > 0)
                    {
                        result.Add(new patch3Set
                        {
                            side = hunk.side,
                            offset = hunk.file2offset,
                            length = hunk.file2length
                        });
                    }
                }
                else
                {
                    // A proper conflict. Determine the extents of the
                    // regions involved from a, o and b. Effectively merge
                    // all the hunks on the left into one giant hunk, and
                    // do the same for the right; then, correct for skew
                    // in the regions of o that each side changed, and
                    // report appropriate spans for the three sides.

                    var regions = new Dictionary<Side, conflictRegion>
                        {
                            {
                                Side.Left,
                                new conflictRegion
                                    {
                                        file1RegionStart = a.Length,
                                        file1RegionEnd = -1,
                                        file2RegionStart = o.Length,
                                        file2RegionEnd = -1
                                    }
                            },
                            {
                                Side.Right,
                                new conflictRegion
                                    {
                                        file1RegionStart = b.Length,
                                        file1RegionEnd = -1,
                                        file2RegionStart = o.Length,
                                        file2RegionEnd = -1
                                    }
                            }
                        };

                    for (int i = firstHunkIndex; i <= hunkIndex; i++)
                    {
                        hunk = hunks[i];
                        var side = hunk.side;
                        var r = regions[side];
                        var oLhs = hunk.file1offset;
                        var oRhs = oLhs + hunk.file1length;
                        var abLhs = hunk.file2offset;
                        var abRhs = abLhs + hunk.file2length;
                        r.file1RegionStart = Math.Min(abLhs, r.file1RegionStart);
                        r.file1RegionEnd = Math.Max(abRhs, r.file1RegionEnd);
                        r.file2RegionStart = Math.Min(oLhs, r.file2RegionStart);
                        r.file2RegionEnd = Math.Max(oRhs, r.file2RegionEnd);
                    }
                    var aLhs = regions[Side.Left].file1RegionStart + (regionLhs - regions[Side.Left].file2RegionStart);
                    var aRhs = regions[Side.Left].file1RegionEnd + (regionRhs - regions[Side.Left].file2RegionEnd);
                    var bLhs = regions[Side.Right].file1RegionStart + (regionLhs - regions[Side.Right].file2RegionStart);
                    var bRhs = regions[Side.Right].file1RegionEnd + (regionRhs - regions[Side.Right].file2RegionEnd);

                    result.Add(new patch3Set
                    {
                        side = Side.Conflict,
                        offset = aLhs,
                        length = aRhs - aLhs,
                        conflictOldOffset = regionLhs,
                        conflictOldLength = regionRhs - regionLhs,
                        conflictRightOffset = bLhs,
                        conflictRightLength = bRhs - bLhs
                    });
                }

                commonOffset = regionRhs;
            }

            copyCommon2(o.Length, ref commonOffset, result);
            return result;
        }

        private static void flushOk(List<string> okLines, List<IMergeResultBlock> result)
        {
            if (okLines.Count > 0)
            {
                var okResult = new MergeOKResultBlock();
                okResult.ContentLines = okLines.ToArray();
                result.Add(okResult);
            }
            okLines.Clear();
        }

        private static bool isTrueConflict(patch3Set rec, string[] a, string[] b)
        {
            if (rec.length != rec.conflictRightLength)
                return true;

            var aoff = rec.offset;
            var boff = rec.conflictRightOffset;

            for (var j = 0; j < rec.length; j++)
            {
                if (a[j + aoff] != b[j + boff])
                    return true;
            }
            return false;
        }

        public static List<IMergeResultBlock> diff3_merge(string[] a, string[] o, string[] b, bool excludeFalseConflicts)
        {
            // Applies the output of Diff.diff3_merge_indices to actually
            // construct the merged file; the returned result alternates
            // between "ok" and "conflict" blocks.

            var result = new List<IMergeResultBlock>();
            var files = new Dictionary<Side, string[]>
                {
                    {Side.Left, a},
                    {Side.Old, o},
                    {Side.Right, b}
                };
            var indices = Diff.diff3_merge_indices(a, o, b);

            var okLines = new List<string>();

            for (var i = 0; i < indices.Count; i++)
            {
                var x = indices[i];
                var side = x.side;
                if (side == Side.Conflict)
                {
                    if (excludeFalseConflicts && !isTrueConflict(x, a, b))
                    {
                        okLines.AddRange(files[0].SliceJS(x.offset, x.offset + x.length));
                    }
                    else
                    {
                        flushOk(okLines, result);
                        result.Add(new MergeConflictResultBlock
                        {
                            LeftLines = a.SliceJS(x.offset, x.offset + x.length),
                            LeftIndex = x.offset,
                            OldLines = o.SliceJS(x.conflictOldOffset, x.conflictOldOffset + x.conflictOldLength),
                            OldIndex = x.conflictOldOffset,
                            RightLines = b.SliceJS(x.conflictRightOffset, x.conflictRightOffset + x.conflictRightLength),
                            RightIndex = x.offset
                        });
                    }
                }
                else
                {
                    okLines.AddRange(files[side].SliceJS(x.offset, x.offset + x.length));
                }
            }

            flushOk(okLines, result);
            return result;
        }

        #endregion
    }

    #region Extra JS-emulating stuff

    public static class ArrayExtension
    {
        public static T[] SliceJS<T>(this T[] array, int startingIndex, int followingIndex)
        {
            if (followingIndex > array.Length)
                followingIndex = array.Length;

            T[] outArray = new T[followingIndex - startingIndex];

            for (var i = 0; i < outArray.Length; i++)
                outArray[i] = array[i + startingIndex];

            return outArray;
        }
    }

    #endregion
}