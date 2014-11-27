using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlashWreckAnalyzer
{
    class ViewModel : INotifyPropertyChanged
    {

        public ViewModel()
        {
            progress.ProgressChanged += HandleProgress;
        }

        private void HandleProgress(object sender, Model.Progress progress)
        {
            Message = progress.Message;
        }

        CancellationTokenSource cts = new CancellationTokenSource();

        Progress<Model.Progress> progress = new Progress<Model.Progress>();

        internal async void RenderFrames()
        {
            var frameCount = FramesToRender;
            Message = "Rendering " + frameCount + " frames...";

            var formattingString = "FlashWreck{0:D8}.png";
            
            await RenderFramesAsync(frameCount, formattingString, cts.Token, new Progress<string>(s=>Message=s));

            Message = "Rendering done. Saved at " + string.Format(formattingString,0);
        }

        private Task RenderFramesAsync(int frameCount, string formattingString, CancellationToken token, IProgress<string> progress )
        {
            return Task.Run(() =>
            {
                var totalCount = Model.Passes.Count;
                for (var frame = 0L; frame < frameCount; ++frame)
                {
                    token.ThrowIfCancellationRequested();
                    long passIndex = totalCount * frame / (frameCount-1);
                    var image = CreateImage(passIndex);
                    var filename = String.Format(formattingString, frame);
                    SaveImageToFile(filename, image);
                    if ((frame&511)==0)
                        progress.Report(String.Format("Image {0}/{1} done, saved as {2}",frame,frameCount,filename));
                }
                progress.Report("All images done.");
            });
        }

        public static void SaveImageToFile(string filePath, BitmapSource image)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
            }
        }
        public async void Load()
        {
            var path = @"C:\Users\Chris\OneDrive\Hypnocube\HypnoController\Staging";


            //var filename1 = @"HCLogfile0005.txt";
            //var filename1 = @"HCLogfile0008.txt";
            //var filename1 = @"HCLogfile0009.txt";
            var filename1 = @"HCLogfile0012.txt";

            if (!File.Exists(Path.Combine(path, filename1)))
                path = @"C:\Users\Chris\SkyDrive\Hypnocube\HypnoController\Staging";


            var model = new Model();


            try
            {
                var passMax = Int32.MaxValue;

                passMax = 1000000;

                await model.ParseFileAsync(
                    Path.Combine(path, filename1),
                    passMax, 
                    cts.Token,
                    progress
                    );

                await model.PrepareInternalDataAsync(
                    cts.Token,
                    progress
                    );

                Model = model;


                var minPass = Model.Passes.First().Pass;
                var maxPass = Model.Passes.Last().Pass;
                var curPass = minPass;
                var sb = new StringBuilder("Missing passes : ");
                int missingPasses = 0;
                foreach (var pass in model.Passes)
                {
                    while (curPass < pass.Pass)
                    {
                        sb.AppendFormat("{0}, ", curPass);
                        ++curPass;
                        ++missingPasses;
                    }
                    while (curPass > pass.Pass)
                    {
                        sb.AppendFormat("{0}, ", curPass);
                        --curPass;
                        ++missingPasses;
                    }
                    ++curPass;
                }
                if (missingPasses > 0)
                { // clean up
                    curPass = Model.Passes.First().Pass;
                    foreach (var pass in Model.Passes)
                    {
                        pass.Pass = curPass++;
                    }
                    MessageBox.Show("Cleaning pass numbering: " + sb.ToString());
                }

                MinPass = model.Passes.Min(p => p.Pass);
                MaxPass = model.Passes.Max(p => p.Pass);
                CurrentPass = MinPass;

            }
            catch (Exception exception)
            {
                MessageBox.Show("Exception: " + exception.ToString());
            }
            
        }

        // colors
        private readonly byte[] colors =
        {
            255,255,255, // page 0, even word, even bit
            224,224,224, // page 0,  odd word,  odd bit
            224,224,224, // page 0,  odd word,  odd bit
            255,255,255, // page 0,  odd word, even bit

            255,255,255, // page 1, even word, even bit
            224,224,224, // page 1, even word,  odd bit
            255,255,255, // page 1,  odd word, even bit
            224,224,224, // page 1,  odd word,  odd bit

        };
        private void UpdateImage()
        {
            var tbmp = CreateImage(CurrentPass);

            Image = tbmp;            

            Message = String.Format("Size {0}x{1}", tbmp.PixelWidth, tbmp.PixelHeight);

        }


        // make HD 1080x720
        // or 1920x1080
        private int sizeX = 1920;
        private int sizeY = 1080;
        private int shiftX = 0;
        private int shiftY = 0;

        // get upper left bit coords, given word, page, bit, and pixel sizes
        void BitCoord(int bitIndex, int pixelX, int pixelY, out int x, out int y)
        {
            var bit = bitIndex & 31;
            var word = (bitIndex/32)&255;
            var page = (bitIndex/32)/256;
            BitCoord(page,word,bit,pixelX,pixelY, out x, out y);

        }

        // get upper left bit coords, given word, page, bit, and pixel sizes
        void BitCoord(int page, int word, int bit, int pixelX, int pixelY, out int x, out int y)
        {
            // 
            x = (word & 3) * 32 + bit;
            y = (word / 4) + page * 64;

            x *= pixelX;
            y *= pixelY;
            y += 2*page;
        }

        // track number of bits at various error counts?
        private const bool trackNErrorBits = false;

        private BitmapSource CreateImage(long pass)
        {
            var px = 14; // pixel sizes
            var py = 7;

            int w, h;
            BitCoord(2, 3, 31, px, py, out w, out h);
            w += px+1;

            //var w = 128*px+1;
            //var h = 128*py+extra+1;
             // center:
            shiftX = (sizeX-w)/2;
            shiftY = (sizeY - h) / 2;

            var pixels = new byte[w*h*4];


            /* track some stats:
             *   a. # of words with N errors, etc.
             *   b. bit errors by position in word 0,1,..,31
             *   c. bit spacing errors in word (dist 1 = neighbors, etc)
             *   d. # of bits with N errors, etc.
             */
            var errorsPerWord     = new int[32]; // a
            var bitErrorPositions = new int[32]; // b
            var bitSpacingErrors  = new int[32]; // c
            // key is # errors, value is number of bits with that error count
            var nErrorBits = new Dictionary<long, long>(); // d

            // track bit errors that happened this pass, draw differently
            var bitErrorsThisPass = new List<int>();
            byte r, g, b;

            // 128 bits wide = 4 words
            // each page 64 bits tall
            for (var page = 0; page < 2; ++page)
            {
                for (var word = 0; word < 256; ++word)
                {
                    var wordErrorCount = 0;

                    uint bitErrorMask = 0; // set bits to 1 where error happened this word
                    for (var bit = 0; bit < 32; ++bit)
                    {
                        var colorIndex = (bit & 1) ^ ((word/4) & 1);

                        colorIndex *= 3;

                        // draw bit
                        int bitX, bitY;
                        BitCoord(page,word,bit,px, py, out bitX,out bitY);
                        r = colors[colorIndex++];
                        g = colors[colorIndex++];
                        b = colors[colorIndex];

                        var bitIndex = page*32*256 + word*32+bit;

                        var bet = Model.ErrorTimes[bitIndex].LastError(pass);
                        if (bet != Model.BitErrors.BitErrorType.None)
                        {
                            var errorsThisBit = Model.ErrorTimes[bitIndex];

                            var errorsSoFarThisBit = errorsThisBit.ErrorCountToHere(pass);

                            // some stats
                            if (trackNErrorBits)
                            {
                                if (!nErrorBits.ContainsKey(errorsSoFarThisBit))
                                    nErrorBits.Add(errorsSoFarThisBit, 0);
                                nErrorBits[errorsSoFarThisBit]++;
                            }

                            wordErrorCount++;
                            bitErrorPositions[bit]++;
                            bitErrorMask |= 1U << bit;

                            // set color based on type and age
                            GetErrorColor(bet, out r, out g, out b, false);

                            // save if just happened this pass
                            if (errorsThisBit.ErrorThisPass(pass) != Model.BitErrors.BitErrorType.None)
                                bitErrorsThisPass.Add(bitIndex);
                        }


                        for (var y = bitY; y < bitY + py; ++y)
                            for (var x = bitX; x < bitX + px; ++x)
                                SetPixel(pixels, w, h, x, y, r, g, b);
                    }

                    TallyBitSpacingErrors(bitSpacingErrors, bitErrorMask);


                    // write box
                    int x1, y1;
                    BitCoord(page, word, 0, px, py, out x1, out y1);
                    var x2 = x1 + 32 * px;
                    var y2 = y1 + py;
                    DrawBox(pixels, w, h, x1, y1, x2, y2, 0, 0, 0);

                    errorsPerWord[wordErrorCount]++;
                }
            }
            
            // draw bit errors this pass
            foreach (var errorIndex in bitErrorsThisPass)
            {
                int ex = 2, ey = 2; // error extra size
                int x, y;
                BitCoord(errorIndex,px,py,out x, out y);
                GetErrorColor(Model.ErrorTimes[errorIndex].LastError(pass), out r, out g, out b, true);
                DrawBox(pixels,w,h,x-ex,y-ey,x+ex+px-1,y+ey+py-1,r,g,b, true);
            }



            // write pixels to a bitmap
            var wb = new WriteableBitmap(sizeX, sizeY, 96.0, 96.0, PixelFormats.Bgr32, null);
            wb.WritePixels(new Int32Rect(0,0,w,h),pixels,4*w,0);

            // format text and other items around the image
            return GetTextBmp(wb, w, h, pass, errorsPerWord, bitErrorPositions, bitSpacingErrors, nErrorBits); 
        }

        private void GetErrorColor(Model.BitErrors.BitErrorType type, out byte r, out byte g, out byte b, bool freshError)
        {
            var t = (byte)(freshError ? 0 : 128);
            switch (type)
            {
                case Model.BitErrors.BitErrorType.EraseOneToZero:
                    r = 255;
                    g = t;
                    b = t;
                    break;
                case Model.BitErrors.BitErrorType.EraseZeroToOne:
                    r = t;
                    g = t;
                    b = 255;
                    break;
                case Model.BitErrors.BitErrorType.WriteOneToZero:
                    r = t;
                    g = 255;
                    b = t;
                    break;
                case Model.BitErrors.BitErrorType.WriteZeroToOne:
                    r = t;
                    g = 255;
                    b = 255;
                    break;
                default :
                    throw new Exception("Unknown error type " + type);
            }
        }

        /// <summary>
        /// Count spacing between bits that are set
        /// </summary>
        /// <param name="bitSpacingErrors"></param>
        /// <param name="bitErrorMask"></param>
        void TallyBitSpacingErrors(int[] bitSpacingErrors, uint bitErrorMask)
        {
            var lastspace = -1;
            var firstspace = -1;

            for (var b = 0; b < 32; ++b)
            {
                if ((bitErrorMask & (1U << b)) != 0)
                {
                    if (firstspace == -1)
                        firstspace = b;
                    else
                        bitSpacingErrors[b-lastspace]++;
                    lastspace = b;
                }
            }
            
            // only 1 bit set, increment spacing 0
            if (firstspace != -1 && firstspace == lastspace)
                bitSpacingErrors[0]++;

        }

        BitmapSource GetTextBmp(WriteableBitmap wb, int w, int h, long pass, int [] errorsPerWord, int[] bitErrorPositions, int[] bitSpacingErrors, Dictionary<long, long> nErrorBits)
        {
            var dv = new DrawingVisual();
            var dc = dv.RenderOpen();
            dc.DrawImage(wb,new Rect(0,0,wb.PixelWidth, wb.PixelHeight));

            var color1 = "`c255,255,255`";
            var color2 = "`c0,255,0`";
            var flashColor = "`c255,255,128`";
            var eraseColor = "`c192,192,192`";
            var nameColor = "`c255,255,192`";

            var emailColor = "`c128,255,128`";
            var urlColor = "`c128,128,255`";

            var passItem = Model.Passes.FirstOrDefault(p => p.Pass >= pass);
            var left = 5.0;
            var top = -5;
            var fontSize = 18.0;
            FormattedText ft;

            // large label
            ft = MakeText(40, Brushes.White, 
                String.Format(
                "{0}Flash torture test  " +
                "{1}Erase/write cycle `c128,128,255`{2,7}`c128,128,128`/`c128,128,255`{3,-7}",
                flashColor,eraseColor,
                pass, Model.Passes.Last().Pass
                ));
            dc.DrawText(ft, new Point(left, h+top));


            var ft2 = MakeText(fontSize, Brushes.White,
                String.Format(
                "{0}Testing 2048 bytes of PIC32MX150F128B flash for erase/write cycle failures " +
                "(each row is 4 words of 32 bits each, 256 words = 64 rows per page, 2 pages, 16384 bits total)",
                eraseColor
                ));
            var splitX = left + ft.Width + 20;
            ft2.MaxTextWidth = sizeX-splitX;
            dc.DrawText(ft2,new Point(splitX, h + top));

            var strings = new List<string>
            {
                String.Format(
                    "{0}Bits erased & written {1}{2} " +
                    "{0}Total errors {1}{3} " +
                    "{0}Elapsed Time {1}{4:dd\\.hh\\:mm\\:ss\\.fff} ",
                    color1, color2,
                    pass*2048,
                    passItem.ErrorCount, 
                    passItem.ElapsedTime
                    ),

            };

            StringBuilder sb;

            sb = new StringBuilder();
            sb.Append("             N =               : ");
            for (var i = 0; i < 32; ++i)
                sb.AppendFormat("{0,5}",i);
            strings.Add(sb.ToString());

            string statColor = "`c192,192,64`";
            strings.Add(String.Format("# words with N bits failed     : {0}{1} ",statColor,MakeStatText(errorsPerWord)));
            strings.Add(String.Format("# bits failed at bit index N   : {0}{1} ",statColor,MakeStatText(bitErrorPositions)));
            strings.Add(String.Format("# errors with bits spaced by N : {0}{1} ",statColor,MakeStatText(bitSpacingErrors)));

            if (trackNErrorBits)
                strings.Add(CreateErrorBitInfo(nErrorBits));

            // legend and names
            sb = new StringBuilder();
            sb.Append("Legend (each drawn larger if happened this cycle): ");
            byte r, g, b;

            GetErrorColor(Model.BitErrors.BitErrorType.EraseOneToZero, out r, out g, out b, false);
            sb.AppendFormat("`c{0},{1},{2}`Erase failed ", r, g, b);
            GetErrorColor(Model.BitErrors.BitErrorType.EraseZeroToOne, out r, out g, out b, false);
            sb.AppendFormat("`c{0},{1},{2}`Erase succeeded after a fail ", r, g, b);
            sb.AppendFormat("`c192,192,192` No recorded write fails");
            //GetErrorColor(Model.BitErrors.BitErrorType.WriteZeroToOne, out r, out g, out b, false);
            //sb.AppendFormat("`c{0},{1},{2}`Write failed ", r, g, b);
            //GetErrorColor(Model.BitErrors.BitErrorType.WriteOneToZero, out r, out g, out b, false);
            //sb.AppendFormat("`c{0},{1},{2}`Write succeeded after a fail ", r, g, b);

            sb.AppendFormat(
                "           " + 
                "{0}Chris Lomont {1}chris@lomont.org " +
                "{0}Gene Foulk {1}gene@hypnocube.com ",
                nameColor,
                emailColor
                );
            
            strings.Add(sb.ToString());


            // misc notes
            strings.Add(
                String.Format(
                "{0}Read details at `u`{1}http://hypnocube.com/2014/11/flash-endurance-testing`n`{0}.    "+
                "{0}Code at `u`{1}https://github.com/ChrisAtHypnocube/FlashWrecker`n`{0}.    " +
                "{0}Visit us at `u`{1}http://www.Hypnocube.com`n`",
                eraseColor,urlColor
                )
            
            );

            // draw any text
            var x = left;
            var y = ft.Height+h-10;

            foreach (var st in strings)
            {
                ft = MakeText(fontSize, Brushes.White, st);
                dc.DrawText(ft, new Point(x, y));
                y += ft.Height-1;
            }



            DrawErrorAndPassBars(w, h, pass, fontSize, dc, passItem);

            dc.Close();

            var bmp = new RenderTargetBitmap(sizeX,sizeY,96.0,96.0,PixelFormats.Pbgra32);
            bmp.Render(dv);

            return bmp;
        }

        /// <summary>
        /// Create string giving stats on how many times each bit failed/succeeded
        /// 
        /// </summary>
        /// <param name="nErrorBits"></param>
        /// <returns></returns>
        private string CreateErrorBitInfo(Dictionary<long, long> nErrorBits)
        {

            var sb = new StringBuilder("#errors->#bits     : ");


            foreach (var key in nErrorBits.Keys.OrderBy(n => n))
            {
                var errors = key;
                var count = nErrorBits[key];
                sb.AppendFormat("{0}->{1} ", errors, count);
            }
            return sb.ToString();
        }

        private static string MakeStatText(int[] counts )
        {
            var sb = new StringBuilder();
            for (var i = 0; i < counts.Length; ++i)
                sb.AppendFormat("{0,5}", counts[i]);
            return sb.ToString();
        }

        void DrawErrorAndPassBars(int w, int h, long pass, double fontSize, DrawingContext dc, Model.PassRecord passRecordItem)
        {
            // we draw in region w,0 - sizeX,h

            var spacing = 10; // pixels around each item
            int linesTop = 3; // lines of text at top
            int linesBottom = 4; // lines of text at bottom
            var passWidth = 10.0; // pen width for pass

            
            var emptyBrush = Brushes.DarkGray;
            var passBrush = Brushes.LightBlue;
            var errorBrush = Brushes.LightPink;

           
            FormattedText ft;
            // draw pass bar on right
            var topSpace = linesTop * fontSize + 2 * spacing;
            var bottomSpace = linesBottom * fontSize + 2 * spacing;
            var x1 = w + spacing;
            var y1 = topSpace;
            var y2 = h - bottomSpace;
            long lp = Model.Passes.Last().Pass;
            long fp = Model.Passes.First().Pass;
            var ratio = (double) (pass - fp)/(lp - fp);
            var y3 = (1 - ratio)*(y2 - y1) + y1;
            dc.DrawLine(new Pen(emptyBrush, passWidth),
                new Point(x1, y1),
                new Point(x1, y2)
                );
            dc.DrawLine(new Pen(passBrush, passWidth),
                new Point(x1, y2),
                new Point(x1, y3)
                );

            var color = "`c255,255,255`";
            ft = MakeText(fontSize, passBrush, String.Format("Erase/Write cycle {0}{1}", color,pass));
            ft.MaxTextWidth = sizeX - x1;
            dc.DrawText(ft, new Point(x1, y2 + spacing));


            // draw error bar on right
            var x2 = sizeX - spacing;
            var bitsFailed = (long)passRecordItem.UniqueBitsFailed;

            DrawErrorBars(dc, x1 + passWidth + 2*spacing, y1, x2, y2, emptyBrush, y3, errorBrush);

            ft = MakeText(fontSize, errorBrush, String.Format("Bits failed {0}{1}", color,bitsFailed));
            ft.MaxTextWidth = sizeX - x1;
            dc.DrawText(ft, new Point(x1, spacing));
        }


        // count pixels wide for error bars
        private double [] errorBarWidths;

        void CreateErrorBarWidths(double x1, double y1, double x2, double y2)
        {
            var ht = (int)Math.Ceiling(y2 - y1+1);
            if (errorBarWidths != null && errorBarWidths.Length == ht)
                return; // already done
            var lowError = Model.Passes.First().UniqueBitsFailed;
            var highError = Model.Passes.Last().UniqueBitsFailed;
            var passCount = Model.Passes.Count;

            errorBarWidths = new double[ht];
            var w = x2 - x1;
            for (var i = 0; i < ht; ++i)
            {
                var passIndex = (ht-1-i)*passCount/ht;
                var pass = Model.Passes[passIndex];
                var ec = pass.UniqueBitsFailed;
                var errWidth = w*(ec - lowError)/(highError - lowError);
                if (errWidth < 0.5 && 0 < errWidth)
                    errWidth = 0.5;
                errorBarWidths[i] = errWidth;
            }
        }

        private void DrawErrorBars(DrawingContext dc, double x1, double y1, double x2, double y2, SolidColorBrush emptyBrush, double y3, SolidColorBrush errorBrush)
        {
            CreateErrorBarWidths(x1,y1,x2,y2);
            var pen1 = new Pen(emptyBrush,1.0);
            var pen2 = new Pen(errorBrush,1.0);
            for (var y = y1; y <= y2; ++y)
            {
                var width = errorBarWidths[(int)(y-y1)];
                var pen = (y < y3) ? pen1 : pen2;
                dc.DrawLine(pen,
                    new Point(x2, y),
                    new Point(x2-width, y)
                    );
            }
        }

        
        internal async Task<string> MakeStatsAsync()
        {
            var statName = String.Format("FlashStats_{0:00000000}.txt", CurrentPass);
            await Model.MakeStatsAsync(statName, CurrentPass, cts.Token, progress);
            return statName;
        }


        Regex textRegex = new Regex(@"`[^`]+?`", // minimal match the pair of ` with at least one item between
            RegexOptions.Compiled
            );
        Regex colorRegex = new Regex(@"(?<red>\d+),(?<green>\d+),(?<blue>\d+)", // 
            RegexOptions.Compiled
            );

        /// <summary>
        /// Basic text formatter:
        /// each backtick ` marks a command, which ends at the next backtick `
        /// double backticks `` become one `
        /// Commands are single letter, then options
        ///    s = font size followed by double value
        ///    c = color followed by R,G,B
        ///    u = underline
        ///    n = normal
        /// </summary>
        /// <param name="fontSize"></param>
        /// <param name="color"></param>
        /// <param name="format"></param>
        /// <returns></returns>
        private FormattedText MakeText(double fontSize, Brush color, string format, params object [] args)
        {
            var controlText = String.Format(format,args);
            // now split out commands, and log them
            var cleanedText = textRegex.Replace(controlText, "");
            // now replace any double `` with single `
            cleanedText = cleanedText.Replace("``", "`");

            var ft = new FormattedText(cleanedText,
                new CultureInfo("en-us"),
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), 
                    FontStyles.Normal,
                    FontWeights.Bold, 
                    new FontStretch()),
                fontSize,
                color,
                new NumberSubstitution(),
                TextFormattingMode.Ideal
                );

            // walk controlText and do any commands 
            // todo - does not deal with `` properly
            Match match;
            do
            {
                match = textRegex.Match(controlText);
                if (match.Success)
                {
                    var matchText = match.Value;
                    var command = Char.ToLower(matchText[1]);
                    var commandArgs = matchText.Substring(2,matchText.Length-3);
                    controlText = controlText.Remove(match.Index, match.Length);
                    
                    ChangeTextFormatting(ft, command, commandArgs, match.Index);
                }
            } while (match.Success);

            // http://msdn.microsoft.com/en-us/library/ms752098(v=vs.110).aspx
            // note can change character items on a per character basis with
            // ft.SetFontSize, .SetFontWeight, .SetForeGroundBrush, .SetFontStyle, 
            // also can outline, etc.

            return ft;
        }

        private void ChangeTextFormatting(FormattedText ft, char command, string commandArgs, int index)
        {
            if (command == 's')
            {
                double size;
                size = Double.Parse(commandArgs);
                ft.SetFontSize(size, index, ft.Text.Length - index);
            }
            if (command == 'u')
            {
                // underline
                var dec = new TextDecorationCollection();
                dec.Add(TextDecorations.Underline);
                ft.SetTextDecorations(dec,index, ft.Text.Length - index);
            }
            if (command == 'n')
            {
                var dec = new TextDecorationCollection();
                ft.SetTextDecorations(dec, index, ft.Text.Length - index);
            }
            if (command == 'c')
            {
                var colorMatch = colorRegex.Match(commandArgs);
                byte r, g, b;
                if (!colorMatch.Success ||
                    !Byte.TryParse(colorMatch.Groups["red"].Value, out r) ||
                    !Byte.TryParse(colorMatch.Groups["green"].Value, out g) ||
                    !Byte.TryParse(colorMatch.Groups["blue"].Value, out b)
                    )
                    throw new Exception("Color did not parse");
                ft.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(r, g, b)), index, ft.Text.Length - index);
            }
        }

        private void DrawBox(byte[] pixels, int w, int h, int x1, int y1, int x2, int y2, byte r, byte g, byte b, bool filled = false)
        {
            for (var y = y1; y <= y2; y++)
            {
                SetPixel(pixels, w, h, x1, y, r, g, b);
                SetPixel(pixels, w, h, x2, y, r, g, b);
            }
            for (var x = x1; x <= x2; x++)
            {
                SetPixel(pixels, w, h, x, y1, r, g, b);
                SetPixel(pixels, w, h, x, y2, r, g, b);

            }
            if (filled)
            {
                for (var y = y1+1; y <= y2-1; y++)
                    for (var x = x1+1; x <= x2-1; x++)
                        SetPixel(pixels, w, h, x, y, r, g, b);
            }
        }

        private void SetPixel(byte[] pixels, int w, int h, int x, int y, byte r, byte g, byte b)
        {
            //x += 2;
            //y += 2; // border
            var index = (y*w+x)*4;
            if (index < 0 || index + 3 > pixels.Length)
                return;
            pixels[index++] = b;
            pixels[index++] = g;
            pixels[index  ] = r;
        }

        #region FramesToRender Property
        private int framesToRender = 60;
        /// <summary>
        /// Gets or sets the number of frames to render.
        /// </summary>
        public int FramesToRender
        {
            get { return framesToRender; }
            set
            {
                // return true if there was a change.
                SetField(ref framesToRender, value);
            }
        }
        #endregion


        #region CurrentPass Property
        private int curPass = 0;
        /// <summary>
        /// Gets or sets property description...
        /// </summary>
        public int CurrentPass
        {
            get { return curPass; }
            set
            {
                // return true if there was a change.
                if (SetField(ref curPass, value))
                    UpdateImage();
            }
        }

        #endregion


        #region MinPass Property
        private int minPass = 0;
        /// <summary>
        /// Gets or sets the minimum pass of the test.
        /// </summary>
        public int MinPass
        {
            get { return minPass; }
            set
            {
                // return true if there was a change.
                SetField(ref minPass, value);
            }
        }
        #endregion

        #region MaxPass Property
        private int maxPass = 100;
        /// <summary>
        /// Gets or sets property description...
        /// </summary>
        public int MaxPass
        {
            get { return maxPass; }
            set
            {
                // return true if there was a change.
                SetField(ref maxPass, value);
            }
        }
        #endregion



        #region Image Property
        private BitmapSource image = null;
        /// <summary>
        /// Gets or sets property description...
        /// </summary>
        public BitmapSource Image
        {
            get { return image; }
            set
            {
                // return true if there was a change.
                SetField(ref image, value);
            }
        }
        #endregion


        #region Message Property
        private string message = "";
        /// <summary>
        /// Gets or sets property description...
        /// </summary>
        public string Message
        {
            get { return message; }
            set
            {
                // return true if there was a change.
                SetField(ref message, value);
            }
        }
        #endregion

        #region Model Property
        private Model model = null;
        /// <summary>
        /// Gets or sets the current view model.
        /// </summary>
        public Model Model
        {
            get { return model; }
            set
            {
                // return true if there was a change.
                SetField(ref model, value);
            }
        }
        #endregion


        #region INotifyPropertyChanged Members

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Safely raises the property changed event.
        /// </summary>
        /// <param name="propertyName">The name of the property to raise.</param>
        protected virtual void NotifyPropertyChanged(string propertyName)
        {
            this.VerifyPropertyName(propertyName); // this is only called in Debug
            var handler = PropertyChanged;
            if (handler != null) { handler(this, new PropertyChangedEventArgs(propertyName)); }
        }

        /// <summary>
        /// Safely raises the property changed event.
        /// </summary>
        /// <param name="selectorExpression">An expression like ()=>PropName giving the name of the property to raise.</param>
        protected virtual void NotifyPropertyChanged<T>(Expression<Func<T>> selectorExpression)
        {
            if (selectorExpression == null)
                throw new ArgumentNullException("selectorExpression");
            var body = selectorExpression.Body as MemberExpression;
            if (body == null)
                throw new ArgumentException("The body must be a member expression");
            NotifyPropertyChanged(body.Member.Name);
        }


        /// <summary>
        /// While in debug, check a string to make sure it is a valid property name. 
        /// </summary>
        /// <param name="propertyName">The name of the property to check.</param>
        [System.Diagnostics.Conditional("DEBUG")]
        private void VerifyPropertyName(string propertyName)
        {
            var type = this.GetType();
            var propInfo = type.GetProperty(propertyName);
            System.Diagnostics.Debug.Assert(propInfo != null, propertyName + " is not a property of " + type.FullName);
        }

        /// <summary>
        /// Set a field if it is not already equal. Return true if there was a change.
        /// </summary>
        /// <param name="field">The field backing to update on change</param>
        /// <param name="value">The new value</param>
        /// <param name="propertyName">The member name, filled in automatically in C# 5.0 and higher.</param>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            // avoid possible infinite loops
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            NotifyPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Set a field if it is not already equal. Return true if there was a change.
        /// </summary>
        /// <param name="field">The field backing to update on change</param>
        /// <param name="value">The new value</param>
        /// <param name="selectorExpression">An expression like ()=>PropName giving the name of the property to raise.</param>
        protected bool SetField<T>(ref T field, T value, Expression<Func<T>> selectorExpression)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            NotifyPropertyChanged(selectorExpression);
            return true;
        }

        #endregion


    }
}



