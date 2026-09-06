// 큐 시트 6장 후처리: 선행 무음 제거 / 시작 클릭 시 짧은 페이드 인 / 끝 페이드 아웃 /
// 같은 큐 변형끼리 체감 음량(RMS) 정렬 / 피크 클리핑 방지.
// 스테레오 인터리브를 그대로 보존한다. 디노이즈는 하지 않는다(노이즈가 본체다).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

static class WavProcess
{
    class Wav { public short[] Data; public int Rate; public int Ch; public int Frames; }

    static string Enc(byte[] b, int o) { return "" + (char)b[o] + (char)b[o + 1] + (char)b[o + 2] + (char)b[o + 3]; }

    static Wav Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int pos = 12, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
        while (pos + 8 <= b.Length)
        {
            string id = Enc(b, pos);
            int sz = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
            else if (id == "data") { dataOff = pos + 8; dataLen = sz; }
            pos += 8 + sz + (sz % 2);
        }
        if (dataOff < 0 || bits != 16) throw new Exception("need 16-bit PCM");
        if (dataOff + dataLen > b.Length) dataLen = b.Length - dataOff;
        int total = dataLen / 2;
        var d = new short[total];
        for (int i = 0; i < total; i++) d[i] = BitConverter.ToInt16(b, dataOff + i * 2);
        return new Wav { Data = d, Rate = rate, Ch = ch, Frames = total / ch };
    }

    static void Write(string path, short[] d, int rate, int ch)
    {
        int bytes = d.Length * 2, align = ch * 2;
        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("RIFF".ToCharArray()); bw.Write(36 + bytes);
            bw.Write("WAVE".ToCharArray()); bw.Write("fmt ".ToCharArray());
            bw.Write(16); bw.Write((short)1); bw.Write((short)ch);
            bw.Write(rate); bw.Write(rate * align); bw.Write((short)align); bw.Write((short)16);
            bw.Write("data".ToCharArray()); bw.Write(bytes);
            foreach (short v in d) bw.Write(v);
        }
    }

    // 2차 버터워스 하이패스. 8비트 소재의 과다 저역(큐 시트 §5 "큰 저역 임팩트")을 걷어낼 때만 쓴다.
    // 노이즈 질감 자체는 건드리지 않으므로 디노이즈 금지 조항에 걸리지 않는다.
    static void HighPass(double[] x, int rate, double fc)
    {
        double w = Math.Tan(Math.PI * fc / rate), k = 1.4142135623730951;
        double n0 = 1 / (1 + k * w + w * w);
        double b0 = n0, b1 = -2 * n0, b2 = n0;
        double a1 = 2 * (w * w - 1) * n0, a2 = (1 - k * w + w * w) * n0;
        double z1 = 0, z2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = x[i];
            double y = b0 * v + z1;
            z1 = b1 * v - a1 * y + z2;
            z2 = b2 * v - a2 * y;
            x[i] = y;
        }
    }

    static void Main(string[] args)
    {
        // args: <inDir> <outDir> [--hp <Hz>] [--suffix <s>] <cuePrefix>=<targetRmsDb> ...
        string inDir = args[0], outDir = args[1];
        var targets = new Dictionary<string, double>();
        double hpHz = 0; string suffix = ""; bool transient = false;
        double maxLenSec = 0; int fadeOutMs = 10;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--hp") { hpHz = double.Parse(args[++i], CultureInfo.InvariantCulture); continue; }
            if (args[i] == "--suffix") { suffix = args[++i]; continue; }
            // 임팩트음 모드. 전체 RMS 로 맞추면 조용한 꼬리가 평균을 끌어내려
            // 말도 안 되는 게인이 필요해지고, 피크 상한에 막혀 결국 작게 남는다.
            if (args[i] == "--transient") { transient = true; continue; }
            // 긴 잔향을 연출 길이에 맞춰 자를 때. 자른 자리는 --fadeout 으로 부드럽게 덮는다.
            if (args[i] == "--maxlen") { maxLenSec = double.Parse(args[++i], CultureInfo.InvariantCulture); continue; }
            if (args[i] == "--fadeout") { fadeOutMs = int.Parse(args[++i], CultureInfo.InvariantCulture); continue; }
            string[] kv = args[i].Split('=');
            targets[kv[0]] = double.Parse(kv[1], CultureInfo.InvariantCulture);
        }
        Directory.CreateDirectory(outDir);

        Console.WriteLine("file,inRmsDb,gainDb,outRmsDb,outPeakDb,fadeInMs,fadeOutMs,trimmedMs");
        foreach (string f in Directory.GetFiles(inDir, "*.wav"))
        {
            string name = Path.GetFileName(f);
            double target = double.NaN;
            foreach (var kv in targets) if (name.Contains(kv.Key)) target = kv.Value;
            if (double.IsNaN(target)) continue;

            Wav w = Read(f);
            int ch = w.Ch, nf = w.Frames;

            // 프레임 단위 최대 절대값 (선행 무음 판정용)
            var amp = new double[nf];
            double peak = 0;
            for (int i = 0; i < nf; i++)
            {
                double m = 0;
                for (int c = 0; c < ch; c++) m = Math.Max(m, Math.Abs(w.Data[i * ch + c] / 32768.0));
                amp[i] = m; if (m > peak) peak = m;
            }

            // 1) 선행 무음 제거: 피크 대비 -40 dB 를 처음 넘는 프레임까지 잘라낸다.
            double thr = peak * 0.01;
            int start = 0; while (start < nf && amp[start] < thr) start++;
            if (start > 0) start = Math.Max(0, start - w.Rate / 1000); // 1ms 여유
            int newFrames = nf - start;
            // 1a) 뒤쪽 디지털 무음 제거 + 요청 시 최대 길이 제한.
            //     라이브러리 음원은 뒤에 몇 초씩 무음이 붙어 오는 경우가 있다.
            int endF = newFrames;
            while (endF > 1 && amp[start + endF - 1] < thr) endF--;
            newFrames = Math.Min(newFrames, endF + w.Rate / 50);   // 20ms 여유
            if (maxLenSec > 0) newFrames = Math.Min(newFrames, (int)(maxLenSec * w.Rate));

            var d = new short[newFrames * ch];
            Array.Copy(w.Data, start * ch, d, 0, newFrames * ch);
            double trimmedMs = 1000.0 * start / w.Rate;

            // 1b) 요청 시 채널별 하이패스. 인터리브를 풀어 채널마다 따로 건다.
            if (hpHz > 0)
            {
                for (int c = 0; c < ch; c++)
                {
                    var lane = new double[newFrames];
                    for (int i = 0; i < newFrames; i++) lane[i] = d[i * ch + c] / 32768.0;
                    HighPass(lane, w.Rate, hpHz);
                    for (int i = 0; i < newFrames; i++)
                    {
                        double v = lane[i];
                        if (v > 0.999969) v = 0.999969; if (v < -1.0) v = -1.0;
                        d[i * ch + c] = (short)Math.Round(v * 32767.0);
                    }
                }
            }

            // 2) 목표 게인. transient 모드에서는 "가장 큰 20ms 구간의 RMS" 를 맞춘다 —
            //    체감 크기는 피크도, 전체 평균도 아니라 이 값을 따라간다.
            double refLevel;
            if (transient)
            {
                int winF = (int)(0.020 * w.Rate);
                double best = 0;
                for (int wf = 0; wf + winF <= newFrames; wf += Math.Max(1, winF / 4))
                {
                    double s2 = 0;
                    for (int i = wf * ch; i < (wf + winF) * ch; i++) { double v = d[i] / 32768.0; s2 += v * v; }
                    double r = Math.Sqrt(s2 / (winF * ch));
                    if (r > best) best = r;
                }
                refLevel = best;
            }
            else
            {
                double sq = 0;
                for (int i = 0; i < d.Length; i++) { double v = d[i] / 32768.0; sq += v * v; }
                refLevel = Math.Sqrt(sq / d.Length);
            }
            double inRmsDb = 20 * Math.Log10(Math.Max(refLevel, 1e-12));
            double gain = Math.Pow(10, (target - inRmsDb) / 20.0);

            double newPeak = 0;
            for (int i = 0; i < d.Length; i++) newPeak = Math.Max(newPeak, Math.Abs(d[i] / 32768.0));
            double ceiling = Math.Pow(10, -0.5 / 20.0);
            // transient 모드는 피크를 tanh 로 눌러 밀도를 얻는다(하드 클립 금지).
            // 일반 모드는 지금까지처럼 게인 자체를 낮춰 피크를 지킨다.
            if (!transient && newPeak * gain > ceiling) gain = ceiling / newPeak;

            // 3) 시작 클릭이 있을 때만 3ms 페이드 인 / 끝은 항상 10ms 페이드 아웃.
            double firstAbs = 0;
            for (int c = 0; c < ch; c++) firstAbs = Math.Max(firstAbs, Math.Abs(d[c] / 32768.0));
            int fadeIn = (firstAbs * gain > Math.Pow(10, -30.0 / 20.0)) ? w.Rate * 3 / 1000 : 0;
            int fadeOut = Math.Min(w.Rate * fadeOutMs / 1000, newFrames / 2);

            var outD = new short[d.Length];
            double outSq = 0, outPeak = 0;
            for (int i = 0; i < newFrames; i++)
            {
                double g = gain;
                if (fadeIn > 0 && i < fadeIn) g *= (double)i / fadeIn;
                int fromEnd = newFrames - 1 - i;
                if (fromEnd < fadeOut) g *= (double)fromEnd / fadeOut;
                for (int c = 0; c < ch; c++)
                {
                    double v = d[i * ch + c] / 32768.0 * g;
                    if (transient) v = 0.94 * Math.Tanh(v / 0.94);
                    if (v > 0.999969) v = 0.999969; if (v < -1.0) v = -1.0;
                    outD[i * ch + c] = (short)Math.Round(v * 32767.0);
                    outSq += v * v; outPeak = Math.Max(outPeak, Math.Abs(v));
                }
            }

            string outName = name.Replace("_cand", "_proc");
            if (suffix.Length > 0) outName = Path.GetFileNameWithoutExtension(outName) + suffix + ".wav";
            string outPath = Path.Combine(outDir, outName);
            Write(outPath, outD, w.Rate, ch);

            double outRmsDb = 20 * Math.Log10(Math.Sqrt(outSq / outD.Length));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F2},{2:F2},{3:F2},{4:F2},{5},{6},{7:F1}",
                Path.GetFileName(outPath), inRmsDb, 20 * Math.Log10(gain), outRmsDb,
                20 * Math.Log10(outPeak), 1000 * fadeIn / w.Rate, 1000 * fadeOut / w.Rate, trimmedMs));
        }
    }
}
