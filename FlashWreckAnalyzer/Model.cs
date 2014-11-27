// #define OLDMODE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;


namespace FlashWreckAnalyzer
{
    public class Model
    {
        public Model()
        {
            Passes = new List<PassRecord>();
            ErrorLines =new List<string>();
            ErrorTimes = new List<BitErrors>();
            for (var i =0; i < 128*128; ++i)
                ErrorTimes.Add(new BitErrors(i));
        }
        /// <summary>
        /// Lines read from input file that were not parseable
        /// </summary>
        public List<string> ErrorLines { get; private set; }
        /// <summary>
        /// All data from passes is read into here
        /// </summary>
        public List<PassRecord> Passes { get; private set; }

        /// <summary>
        /// For each bit position, track error information
        /// </summary>
        public List<BitErrors> ErrorTimes { get; private set; }

        public class Progress
        {                                      
            public string Message { get; set; }
        }

        /// <summary>
        /// Call this to load each file
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="maxPass">can be Int32.MaxValue</param>
        public Task ParseFileAsync(
            string filename, 
            int maxPass, 
            CancellationToken cancellationToken,
            IProgress<Progress> progress
            )
        {
            return Task.Run(() => ParseFile(
                filename, maxPass, cancellationToken, progress
                ));
        }


        class FileSplitter
        {

            private IEnumerable<String> fileLines;
            private IEnumerator<string> fileEnumerator; 
            private List<string> tempLines;
            private string curLine;
            
            public FileSplitter(string filename)
            {
                fileLines = File.ReadLines(filename);
                UnmatchedLines = new List<string>();
                tempLines  = new List<string>();
                fileEnumerator = fileLines.GetEnumerator();

                curLine = ReadLine();
            }

            private bool fileEmpty = false;

            /// <summary>
            /// Read the next line, update internal counters and temp line
            /// </summary>
            /// <returns></returns>
            private string ReadLine()
            {

                fileEmpty = !fileEnumerator.MoveNext();
                if (fileEmpty)
                    return null; // nothing more
                var line = fileEnumerator.Current;
                tempLines.Add(line);
                LineCount++;
                BytesRead += line.Length;
                return line;
            }

            /// <summary>
            /// Lines from the file that did not match anything
            /// </summary>
            public List<string> UnmatchedLines { get; private set; }

            /// <summary>
            /// Return the next string that matches any of the regexes.
            /// Ensures the match starts at position 0 and does not go to the
            /// end of the string, to make sure strings are not cut short by
            /// premature line breaks.
            /// Return null if nothing left.
            /// matchIndex set to -1 if no matches
            /// </summary>
            /// <returns></returns>
            public string Next(Regex [] regexes, out int matchIndex)
            {
                matchIndex = -1;
                string nextLine = "";
                do
                {
                    // extend line
                    curLine += nextLine;

                    // look for earliest match, make sure it is not all the way to the end
                    // of the line if possible
                    var matches = regexes.
                        Select((r, n) =>
                        new {
                            Match = r.Match(curLine),
                            index = n
                        }).
                        Where(tp=>tp.Match.Success).
                        OrderBy(tp=>tp.Match.Index);

                    foreach (var pair in matches)
                    {
                        //var reg = regexes[i];
                        //var match = reg.Match(curLine);
                        var match = pair.Match;
                        if (!match.Success || !ProcessMatch(match)) continue;
                        matchIndex = pair.index;
                        return match.Value;
                    }
                    // nothing matched, extend the current line if possible, else fails
                    nextLine = ReadLine();
                } while (nextLine != null);

                // move remaining to unmatched lines
                UnmatchedLines.AddRange(tempLines);
                return null; 
            }

            /// <summary>
            /// See if the match meets needed criteria and process result
            /// Return true if it is ok
            /// </summary>
            /// <param name="match"></param>
            /// <returns></returns>
            private bool ProcessMatch(Match match)
            {
                var lastIndex = match.Index + match.Value.Length;
                if (fileEmpty || lastIndex < curLine.Length)
                {   // it's a good match. 
                    
                    // move any lines that didn't match at start
                    // to unmatched lines
                    Move(match.Index,UnmatchedLines);


                    // remove those that matched
                    Move(match.Value.Length,null);
                    return true;
                }
                return false; // not enough matched yet
            }

            /// <summary>
            /// Move text from curLine in range [0,endIndex)
            /// from tempLines and from curLine.
            /// Move to destination if not null
            /// </summary>
            /// <param name="endIndex"></param>
            /// <param name="destination"></param>
            void Move(int endIndex, List<string> destination)
            {
                var left = endIndex;
                while (left>0)
                {

                    var t = tempLines[0];
                    if (t.Length <= left)
                        tempLines.RemoveAt(0); // can take whole thing
                    else
                    {   // take part of temp line
                        tempLines[0] = t.Substring(left);
                        t = t.Substring(0, left);
                    }

                    if (destination != null)
                        destination.Add(t);

                    curLine = curLine.Substring(t.Length); // remove prefix

                    left -= t.Length;

                }

            }
            public long LineCount { get; private set;  }
            public long BytesRead { get; private set; }


        }


        /// <summary>
        /// Call this to load each file
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="maxCount"></param>
        public void ParseFile(
            string filename, int maxPass,
            CancellationToken cancellationToken,
            IProgress<Progress> progress
            )
        {
            var progressValue = new Progress();
            var fi = new FileInfo(filename);
            var fileLength = fi.Length;
            long totalPassCount = 0, totalErrorCount = 0;
            var provider = CultureInfo.InvariantCulture;
            var done = false;

#if OLDMODE
    // var lineCount = 0;
    // long bytesRead = 0;
    //var lines = File.ReadLines(filename);
    //var fileLineAtLastErrorLine = -100L; // this will help fix lines that got split somehow
    //todo - better error line merging
            foreach (var line1 in lines)

            {
#else

            var regexes = new[] {passRegex, errorRegex};
            int matchIndex;
            var fileSplitter = new FileSplitter(filename);
            var line1 = fileSplitter.Next(regexes, out matchIndex);
            do
            {
                var lineCount = fileSplitter.LineCount;
                var bytesRead = fileSplitter.BytesRead;
#endif

                cancellationToken.ThrowIfCancellationRequested();
                if ((lineCount & 511) == 0)
                {
                    progressValue.Message = String.Format(
                        "File parsing phase: {0}/{1} kb read ({2:F2}%), {3} lines read, {4} passes read, {5} errors read, {6} error lines",
                        bytesRead/1024, fileLength/1024,
                        bytesRead*100.0/fileLength,
                        lineCount,
                        totalPassCount,
                        totalErrorCount,
                        ErrorLines.Count
                        );
                    progress.Report(progressValue);
                }

                ++lineCount;
                bytesRead += line1.Length;
                var line = line1;

                while (!done && !String.IsNullOrEmpty(line))
                {
                    var passMatch = passRegex.Match(line);
                    var errMatch = (!passMatch.Success)
                        ? errorRegex.Match(line)
                        : null;
                    if (passMatch.Success)
                    {
                        var pass = Int32.Parse(passMatch.Groups["pass"].Value);
                        var frame = Int32.Parse(passMatch.Groups["frame"].Value);

                        uint offset;
                        if (
                            !UInt32.TryParse(passMatch.Groups["offset"].Value, NumberStyles.HexNumber, provider,
                                out offset))
                            throw new Exception("Illegal format");

                        uint tick;
                        if (!UInt32.TryParse(passMatch.Groups["tick"].Value, NumberStyles.HexNumber, provider, out tick))
                            throw new Exception("Illegal format");

                        ulong errorCount;
                        if (
                            !UInt64.TryParse(passMatch.Groups["errors"].Value, out errorCount))
                            throw new Exception("Illegal format");

                        var passItem = new PassRecord(pass, frame, offset, tick, errorCount);
                        line = passRegex.Replace(line, "", 1);

                        // do this just before last pass to ensure we got all errors
                        if (Passes.Count >= maxPass)
                            done = true;
                        else
                        {
                            Passes.Add(passItem);
                            ++totalPassCount;
                        }
                    }
                    else if (errMatch != null && errMatch.Success)
                    {
                        var lastpass = Passes.LastOrDefault();
                        if (lastpass == null)
                            continue;

                        uint offset;
                        if (
                            !UInt32.TryParse(errMatch.Groups["offset"].Value, NumberStyles.HexNumber, provider,
                                out offset))
                            throw new Exception("Illegal format");
                        uint read;
                        if (!UInt32.TryParse(errMatch.Groups["read"].Value, NumberStyles.HexNumber, provider, out read))
                            throw new Exception("Illegal format");
                        uint desired;
                        if (
                            !UInt32.TryParse(errMatch.Groups["desired"].Value, NumberStyles.HexNumber, provider,
                                out desired))
                            throw new Exception("Illegal format");

                        var errorType = Error.ErrorTypes.Erase;
                        if (errMatch.Groups["type"].Value == "E")
                            errorType = Error.ErrorTypes.Erase;
                            //else if (passMatch.Groups["type"].Value == "W")
                            //    errorType = Error.ErrorTypes.Write;
                        else
                            throw new Exception("Illegal format");
                        lastpass.Errors.Add(new Error(lastpass, offset, read, desired, errorType));
                        totalErrorCount++;
                        line = errorRegex.Replace(line, "", 1);
                    }
#if OLDMODE
                    else
                    {

                        var isErrorLine = true; // assume this line will be an error
                        if (fileLineAtLastErrorLine == lineCount - 1 && ErrorLines.Count > 0)
                        {
                            // back to back, pull out
                            var tempLine = ErrorLines.Last() + line;
                            isErrorLine &= !passRegex.IsMatch(tempLine);
                            isErrorLine &= !errorRegex.IsMatch(tempLine);

                            if (!isErrorLine)
                            {
                                // pull last one out and test it
                                line = tempLine;
                                ErrorLines.RemoveAt(ErrorLines.Count - 1);
                            }
                        }

                        if (isErrorLine)
                        {
                            ErrorLines.Add(line);
                            line = "";
                        }
                        fileLineAtLastErrorLine = lineCount;

                    } // error line
                    
                } // while line not all used up
            }
#else

                } // while line not all used up
                line1 = fileSplitter.Next(regexes, out matchIndex);
            } while (!done && !String.IsNullOrEmpty(line1));
            ErrorLines.AddRange(fileSplitter.UnmatchedLines);
#endif
        }

        /// <summary>
        /// PIC32 ran this many ticks per second
        /// </summary>
        const int PIC32TicksPerSecond = 24000000;

        public Task PrepareInternalDataAsync(CancellationToken cancellationToken, Progress<Progress> progress)
        {
            return Task.Run(() => PrepareInternalData(
                cancellationToken, progress
                ));
        }


        /// <summary>
        /// After all data loaded, call this to finish computing internals
        /// </summary>
        public void PrepareInternalData(CancellationToken cancellationToken, IProgress<Progress> progress)
        {
            /* TODO
             *  DONE 1. At each pass - count number of unique bits with errors
             *  DONE 2. For each bit error, track type (failed erase, failed write, expected 0->1 or 1->0)
             *  DONE 3. Bit error distributions: 
             *  DONE    a. # of words with N errors, etc.
             *  DONE    b. bit errors by position in word 0,1,..,31
             *  DONE    c. bit spacing errors in word (dist 1 = neighbors, etc)
             *  DONE    d. # of bits with N errors, etc.
             * */

            uint lastTick = Passes[0].Tick;
            ulong totalTicks = 0;


            var progressValue = new Progress();
            progress.Report(progressValue);  

            // sort passes by pass index
            var pl = Passes.OrderBy(p => p.Pass).ToList();
            Passes.Clear();
            foreach (var pass in pl)
                Passes.Add(pass);




            int uniqueBitsFailed = 0;
            int passesProcessedCount = 0;
            foreach (var pass in Passes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((passesProcessedCount & 511) == 0)
                {
                    progressValue.Message = String.Format(
                        "Processing pass {0}/{1} ({2:F3}%)",
                        passesProcessedCount,
                        Passes.Count,
                        passesProcessedCount*100.0/Passes.Count
                        );
                    progress.Report(progressValue);
                }
                passesProcessedCount++;

                // compute elaspsed time
                uint delta = pass.Tick - lastTick;
                totalTicks += delta;
                lastTick = pass.Tick;
                pass.ElapsedTime = TimeSpan.FromMilliseconds((double)totalTicks / (PIC32TicksPerSecond / 1000.0));

                // add error information to the bit array of errors
                foreach (var e in pass.Errors)
                {
                    var read = e.Read;
                    var desired = e.Desired;
                    for (var bit = 0; bit < 32; ++bit)
                    {
                        if (((read ^ desired) & 1) == 1)
                        {
                            var errorBit = ErrorTimes[(int) (e.Offset*32 + bit)];
                            
                            errorBit.Errors.Add(e);

                            if (errorBit.FirstFailurePass == pass.Pass)
                                uniqueBitsFailed++;

                        }
                        read >>= 1;
                        desired >>= 1;
                    }
                }

                pass.UniqueBitsFailed = uniqueBitsFailed;
            }
        }


        #region Regular expressions
        Regex passRegex = new Regex(
            // Pass 441871, frame 0, offset 00000000, time ce045cbd, errors 583
            //@"^" + // match start of string
            @"Pass (?<pass>\d+), frame (?<frame>\d{1,2}), offset (?<offset>[0-9a-f]{8}), " +
            @"time (?<tick>[0-9a-f]{8}), errors (?<errors>\d+)",
            RegexOptions.Compiled);

        Regex errorRegex = new Regex(
            // ERROR: (E) offset 0000012B read FFFFFFFF desired FFFFFFF7.
            //@"^" + // match start of string
            @"ERROR: " +
            @"\((?<type>(E|W))\) " +
            @"offset (?<offset>[0-9A-F]{8}) " +
            @"read (?<read>[0-9A-F]{8}) desired (?<desired>[0-9A-F]{8})\." +
            "",
            RegexOptions.Compiled);
        #endregion
                  
        #region Helper classes
        /// <summary>
        /// Errors at this bit by pass number
        /// </summary>
        public class BitErrors
        {
            // todo - rewrite for speed

            public List<Error> Errors = new List<Error>();

            public enum BitErrorType
            {
                None,
                EraseZeroToOne, // desired 0, read 1
                EraseOneToZero, // desired 1, read 0
                WriteZeroToOne, // desired 0, read 1
                WriteOneToZero, // desired 1, read 0
            }

            /// <summary>
            /// Return type of error on this pass
            /// </summary>
            /// <param name="pass"></param>
            /// <returns></returns>
            public BitErrorType ErrorThisPass(long pass)
            {
                var index = IndexToHere(pass);
                if (index == -1)
                    return BitErrorType.None;
                if (Errors[index].PassRecord.Pass != pass)
                    return BitErrorType.None;
                return ComputeErrorType(index);
            }

            /// <summary>
            /// Count of errors on this bit 
            /// up to and including this pass
            /// </summary>
            /// <param name="pass"></param>
            /// <returns></returns>
            public int ErrorCountToHere(long pass)
            {
                if (pass < FirstFailurePass)
                    return 0;
                return IndexToHere(pass);
            }

            /// <summary>
            /// Return index of error at this bit with pass 
            /// up to and including parameter pass
            /// if none, return -1
            /// </summary>
            /// <param name="pass"></param>
            /// <returns></returns>
            int IndexToHere(long pass)
            {
                if (pass < FirstFailurePass)
                    return -1;
                var index = 0;
                while (index < Errors.Count && Errors[index].PassRecord.Pass <= pass)
                    ++index;
                return index-1;
            }


            /// <summary>
            /// return error type that happened at this pass or earlier
            /// </summary>
            /// <param name="pass"></param>
            /// <returns></returns>
            public BitErrorType LastError(long pass)
            {
                if (pass < FirstFailurePass)
                    return BitErrorType.None;
                return ComputeErrorType(IndexToHere(pass));
            }

            public BitErrorType ComputeErrorType(int errorIndex)
            {

                var error = Errors[errorIndex];
                var bi = BitIndex & 31;
                var read = (error.Read>>bi)&1;
                var desired = (error.Desired>>bi)&1;

                if (error.ErrorType == Error.ErrorTypes.Erase)
                {
                    if (read == 0 && desired == 1)
                        return BitErrorType.EraseOneToZero;
                    if (read == 1 && desired == 0)
                        return BitErrorType.EraseZeroToOne;
                }
                if (error.ErrorType == Error.ErrorTypes.Write)
                {
                    if (read == 0 && desired == 1)
                        return BitErrorType.WriteOneToZero;
                    if (read == 1 && desired == 0)
                        return BitErrorType.WriteZeroToOne;
                }
                throw new Exception("Unknown error type");
            }

            public override string ToString()
            {
                return String.Format("{0}", Errors.Count);
            }

            /// <summary>
            /// Return first index there was an error, or if there were none, return
            /// Int32.MaxValue
            /// </summary>
            public int FirstFailurePass {
                get
                {
                    if (minError == Int32.MaxValue && Errors.Count > 0)
                        minError = Errors[0].PassRecord.Pass;
                    return minError;
                }}
            int minError = int.MaxValue;
            
            /// <summary>
            /// Index of this bit in the total array
            /// </summary>
            public int BitIndex { get; private set; }

            public BitErrors(int bitIndex)
            {
                BitIndex = bitIndex;
            }
        }


        public class Error
        {   
            // ERROR: (E) offset 0000012B read FFFFFFFF desired FFFFFFF7.
            public Error(PassRecord pass, uint offset, uint readValue, uint desiredValue, ErrorTypes errorType)
            {
                PassRecord = pass;
                Offset = offset;
                Read = readValue;
                Desired = desiredValue;
                ErrorType = errorType;
            }
            /// <summary>
            /// Memory offset in words
            /// </summary>
            public uint Offset { get; private set; }
            /// <summary>
            /// 32 bit value read at this offset
            /// </summary>
            public uint Read { get; private set;  }
            
            /// <summary>
            /// 32 bit value desired from this offset
            /// </summary>
            public uint Desired { get; private set; }

            /// <summary>
            /// Pass of the erase/write run where this error occurred
            /// </summary>
            public PassRecord PassRecord { get; private set; }

            public enum ErrorTypes
            {
                /// <summary>
                /// Error found after an erase
                /// </summary>
                Erase,
                /// <summary>
                /// Error found after a write
                /// </summary>
                Write
            }

            /// <summary>
            /// Whether this error was found after an erase or after a write
            /// </summary>
            public ErrorTypes ErrorType { get; private set; }
        }

        public class PassRecord
        {
            // Pass 441871, frame 0, offset 00000000, time ce045cbd, errors 583
            public PassRecord(int pass, int frame, uint offset, uint tick, ulong errorCount)
            {
                Pass = pass;
                Frame = frame;
                Tick = tick;
                Offset = offset;
                ErrorCount = errorCount;
                Errors = new List<Error>();
            }
            /// <summary>
            /// Erase/write count
            /// </summary>
            public int Pass { get; set; }

            /// <summary>
            /// Time elapsed from first pass in system
            /// </summary>
            public TimeSpan ElapsedTime { get; set; }
            
            /// <summary>
            /// Frame was which section 0-7 of PIC flash we tested
            /// </summary>
            public int Frame { get; private set; }

            /// <summary>
            /// 32 bit PIC core timer tick
            /// </summary>
            public uint Tick { get; private set; }

            /// <summary>
            /// Start of flash memory from base address, is Frame times some fixed amount
            /// </summary>
            public uint Offset { get; private set; }

            /// <summary>
            /// Total errors seen
            /// </summary>
            public ulong ErrorCount { get; private set; }

            /// <summary>
            /// Number of unique bits that failed so far, including this pass
            /// </summary>
            public int UniqueBitsFailed { get; set; }

            /// <summary>
            /// Errors that occurred on this pass
            /// </summary>
            public List<Error> Errors { get; private set; }

            /// <summary>
            /// Simple view of a pass record
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return String.Format(
                    "Pass {0}, time {6}, frame {1}, offset {2:X8}, ticks {3:X8}, errors {4} ({5})",
                    Pass, Frame, Offset, Tick, ErrorCount,
                    Errors.Count,
                    ElapsedTime
                    );
            }
        }

        #endregion

        /// <summary>
        /// Gather stats on a value, allowing outputting of info
        /// </summary>
        class Stat
        {

            /// <summary>
            /// Output as min,25%,50%,75%,max,avg,count
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                var min = values.Keys.Min();
                var max = values.Keys.Max();
                ToSortedList();
                var length = sortedValues.Count;
                var quarter = sortedValues[length / 4];
                var half = sortedValues[length / 2];
                var threeQuarter = sortedValues[3 * length / 4];
                var avg = (double)sortedValues.Sum() / count;
                return String.Format("{0},{1},{2},{3},{4},{5:F3},{6}",
                    min, quarter, half, threeQuarter, max, avg, count
                    );
            }

            private List<long> sortedValues = new List<long>(); 
            private void ToSortedList()
            {
                if (sortedValues.Count != 0)
                    return;
                foreach (var value in values)
                {
                    for (var i =0 ; i < value.Value; ++i)
                    sortedValues.Add(value.Key);
                }
                sortedValues.Sort();
            }

            public void Record(long length)
            {
                sortedValues.Clear();
                if (!values.ContainsKey(length))
                    values.Add(length,0);
                values[length]++;
                ++count; 
            }

            private long count = 0;
            // for value i, count how many times it occurs
            Dictionary<long,long> values = new Dictionary<long, long>(); 
        }

        /// <summary>
        /// Given the errors on a bit, get the sequence of passes where they happened
        /// Count # of correct toggling success/fail, number of misses
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        List<long> BitToggleSequence(BitErrors error, out long correctToggles, out long missedToggles)
        {
            var nextErrorType = BitErrors.BitErrorType.EraseOneToZero;
            correctToggles = 0;
            missedToggles = 0;
            var seq = new List<long>();
            for (var errorIndex = 0; errorIndex < error.Errors.Count; ++errorIndex)
            {
                var e = error.Errors[errorIndex];
                var et = error.ComputeErrorType(errorIndex);
                if (et == nextErrorType)
                {
                    ++correctToggles;
                    seq.Add(e.PassRecord.Pass); 
                    
                    // toggle type
                    nextErrorType =
                        et == BitErrors.BitErrorType.EraseOneToZero
                            ? BitErrors.BitErrorType.EraseZeroToOne
                            : BitErrors.BitErrorType.EraseOneToZero;
                }
                else
                {
                    ++missedToggles;
                }
            }
            return seq;
        }

        /// <summary>
        /// Write out some stats for the selected pass and some general stats:
        /// 1. Bits failed at any previous time per row (Row is 128 bytes)
        /// 2. List of cycle numbers for each new bit failed and bit index
        /// 3. For each failed bit
        ///    a. Get sequence of fails, successes by cycle, giving sequence of lengths l0,l1,l2,...,lk
        ///    b. l_even are cycles of failed bit, l_odd are cycles of success
        ///    c. while doing so, detect missing toggles, discard bad bits from stats
        ///    d. compute, for each i, stats: [min,25%,50%,75%,max,avg,count], output these
        ///    e. for bits that have a fail and stabilize well before the end of the 
        ///       dataset, count total ending on success, on fail, and stats on length of each type
        ///    f. count total toggles fail/success, missed toggles fail/success
        ///    g. count total error bits, those with no missed toggles, those with missed toggles
        /// 4. Output the earliest failed bit sequence for inspection
        /// 5. Cycle when first bit fail happened
        /// 6. # bits failed at each cycle after first fail
        /// 7. # bits succeeded each cycle 
        /// </summary>
        /// <param name="statName"></param>
        /// <param name="passNumber"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="progress"></param>
        private void MakeStatsHelper(string statName, 
            int passNumber, 
            CancellationToken cancellationToken,
            IProgress<Progress> progress)
        {
            var progressValue = new Progress();


            using (var file = File.CreateText(statName))
            {
                file.WriteLine("Flash bit endurance composite stats");
                file.WriteLine("Erase/write cycle {0}",passNumber);
                file.WriteLine();

                // 1. Bits failed at any previous time per row (Row is 128 bytes)
                progressValue.Message = "Outputting bits failed per row";
                progress.Report(progressValue);
                file.WriteLine("Bits failed on each row of 128 bytes:");
                for (var row = 0; row < 128*128/8/128; ++row)
                {
                    var bitsFailedThisRow = 0;
                    for (var bitIndex = 0; bitIndex < 128*8; ++bitIndex)
                    {
                        if (ErrorTimes[bitIndex + row*128*8].FirstFailurePass <= passNumber)
                            ++bitsFailedThisRow;
                    }
                    file.Write("{0},",bitsFailedThisRow);
                }
                file.WriteLine();
                file.WriteLine();

                // 2. list of cycles where first bit failed
                var failedBits = ErrorTimes.
                    Where(b => b.FirstFailurePass != Int32.MaxValue).
                    OrderBy(b => b.FirstFailurePass).
                    ToList();
                progressValue.Message = "Outputting cycles where a bit first failed";
                progress.Report(progressValue);
                file.WriteLine("{0} bits failed out of {1}. Ordered list of bit failures by cycle:", failedBits.Count, 128 * 128);
                foreach (var error in failedBits)
                    file.Write("{0},", error.FirstFailurePass);
                file.WriteLine();

                file.WriteLine("Failed bit indices, ordered by first failure cycle.");
                foreach (var error in failedBits)
                    file.Write("{0},", error.BitIndex);
                file.WriteLine();
                file.WriteLine();



                // 3. For each failed bit
                //    a. Get sequence of fails, successes by cycle, giving sequence of lengths l0,l1,l2,...,lk
                //    b. l_even are cycles of failed bit, l_odd are cycles of success
                //    c. while doing so, detect missing toggles, discard bad bits from stats
                //    d. compute, for each i, stats: [min,25%,50%,75%,max,avg,count], output these
                //    e. for bits that have a fail then stabilize well before the end of the 
                //       dataset, count total ending on success, on fail, and stats on length of each type
                //    f. count total toggles fail/success, missed toggles fail/success
                //    g. count total error bits, those with no missed toggles, those with missed toggles
                var stat = new Dictionary<long,Stat>(); // maps index to a stat for that index
                var stableEndOnFailStats = new Stat();
                var stableEndOnSuccessStats = new Stat();
                var stableCutoffLength = 100000; // if last state ok this many cycles, gather more data
                long totalMissedToggles = 0, totalCorrectToggles = 0, completeBits = 0;
                file.WriteLine("Stats marked with a (*) are of the form [min,25%,50%,75%,max,avg,count]...");

                progressValue.Message = "Gathering per bit stats";
                progress.Report(progressValue);

                foreach (var errorBit in failedBits)
                {
                    long missedToggles = 0L, correctToggles = 0L;
                    var seq = BitToggleSequence(errorBit, out correctToggles, out missedToggles);
                    totalMissedToggles += missedToggles;
                    totalCorrectToggles += correctToggles;
                    if (missedToggles == 0)
                    { // this one good for bit stats
                        completeBits ++;
                        for (var i = 0; i < seq.Count-1; ++i)
                        {
                            var length = seq[i + 1] - seq[i];
                            if (!stat.ContainsKey(i))
                                stat.Add(i,new Stat());
                            stat[i].Record(length);
                        }
                        // final long sequence
                        var finalLength = Passes.Count - seq.Last();
                        if (!stat.ContainsKey(seq.Count))
                            stat.Add(seq.Count, new Stat());
                        stat[seq.Count].Record(finalLength);
                        if (finalLength > stableCutoffLength)
                        {
                            var totalLength = seq.Last() - seq.First();
                            var endOnFail = (seq.Count & 1) != 0; // odd length ends on fail
                            if (endOnFail)
                                stableEndOnFailStats.Record(totalLength);
                            else
                                stableEndOnSuccessStats.Record(totalLength);
                        }

                    }
                }


                //    d. compute, for each i, stats: [min,25%,50%,75%,max,count], output these
                file.WriteLine("For each failed bit, lengths of failed mode, from first to last, stats (*):");
                foreach (var st in stat.Keys.OrderBy(n => n).Where(n=>(n&1)==1))
                    file.Write("{0},",stat[st]);
                file.WriteLine();
                file.WriteLine();

                file.WriteLine("For each failed bit, lengths of working mode, from first to last, stats (*):");
                foreach (var st in stat.Keys.OrderBy(n => n).Where(n => (n & 1) == 0))
                    file.Write("{0},", stat[st]);
                file.WriteLine();
                file.WriteLine();
                    

                //    e. for bits that have a fail then stabilize well before the end of the 
                //       dataset, count total ending on success, on fail, and stats on length of each type
                file.WriteLine("Total length of fail/success toggles for bits that ended on a fail    before {0} cycles before end stats (*): {1}", stableCutoffLength, stableEndOnFailStats);
                file.WriteLine("Total length of fail/success toggles for bits that ended on a success before {0} cycles before end stats (*): {1}", stableCutoffLength, stableEndOnSuccessStats);

                //    f. count total toggles fail/success, missed toggles fail/success
                //    g. count total error bits, those with no missed toggles, those with missed toggles
                file.WriteLine("Total correct toggles {0}, total missed toggles {1}, total bits {2}, bits with all correct toggles {3}",
                    totalCorrectToggles,
                    totalMissedToggles,
                    failedBits.Count,
                    completeBits
                    );
                
                // 4. Output the earliest failed bit sequence for inspection
                // 5. Cycle when first bit fail happened
                progressValue.Message = "Outputting first failed bit sequence";
                progress.Report(progressValue);
                if (failedBits.Any())
                {
                    var firstFailedBit = failedBits[0];
                    file.WriteLine(
                        "First failed bit happened on cycle {0} at bit index {1}.",
                        firstFailedBit.FirstFailurePass, firstFailedBit.BitIndex
                        );
                    file.WriteLine("List of all errors on this bit as cycle,error type:");

                    long missedToggles = 0L, correctToggles = 0L;
                    var seq = BitToggleSequence(firstFailedBit, out correctToggles, out missedToggles);
                    foreach (var s in seq)
                        file.Write("{0},", s);
                    file.WriteLine();
                    file.WriteLine("{0} missed error toggles failed<->success detected", missedToggles);
                    file.WriteLine();
                } // if any failed bits, 
                
                file.WriteLine();
                file.WriteLine();

#if true

                // 6. # bits failed at each cycle after first fail
                // 7. # bits succeeded each cycle 
                progressValue.Message = "Good/success/fail bit tallying";
                progress.Report(progressValue);


                file.WriteLine("Tuples of: pass #, bits never failed, bits failed this pass, bits restored from a fail this pass, :");

                for (var i = 0; i < Passes.Count; ++i)
                {
                    var writePass = false;
                    var passIndex = Passes[i].Pass;

                    foreach (var error in failedBits)
                    {
                        writePass |= error.ErrorThisPass(passIndex) != BitErrors.BitErrorType.None;
                        if (error.FirstFailurePass > passIndex)
                            break;
                    }
                    if (writePass || i == 0 || i == Passes.Count - 1)
                    {
                        var currentlyFailedBitCount = 0;
                        var currentlyRestoredBitCount = 0;

                        foreach (var error in failedBits)
                        {
                            writePass |= error.ErrorThisPass(passIndex) != BitErrors.BitErrorType.None;
                            if (error.FirstFailurePass > passIndex)
                                break;
                        if (error.LastError(passIndex) == BitErrors.BitErrorType.EraseOneToZero)
                            currentlyFailedBitCount++;
                        if (error.LastError(passIndex) == BitErrors.BitErrorType.EraseZeroToOne)
                            currentlyRestoredBitCount++;
                        }
                        var neverFailedBitCount = 128 * 128 - currentlyFailedBitCount - currentlyRestoredBitCount;

                        file.Write("{0},{1},{2},{3},",
                            passIndex,
                            neverFailedBitCount,
                            currentlyFailedBitCount,
                            currentlyRestoredBitCount
                            );
                    }
                }
                file.WriteLine();
                file.WriteLine();
#endif


                progressValue.Message = "Stats output finished, saved at " + statName;
                progress.Report(progressValue);

            }
        }


        internal Task MakeStatsAsync(string statName, int passNumber, CancellationToken cancellationToken, IProgress<Progress> progress)
        {
            return Task.Run(() => MakeStatsHelper(statName, passNumber, cancellationToken, progress));
        }
    }
}
