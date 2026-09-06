// 화면 노이즈 후보 WAV 를 큐 시트 5장 "탈락 조건" 기준으로 기계 검사한다.
// 측정: 시작/끝 클릭, 대역 에너지 분포, 스펙트럼 평탄도(톤성), 협대역 피크(비프/공진),
//       엔벨로프 주기성(디지털 스터터), 엔벨로프 기울기(긴장 상승 여부).
using System;
using System.Collections.Generic;
using System.IO;

static class WavAnalyze
{
    struct Wav { public double[] S; public int Rate; public int Ch; }

    static Wav Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 44 || Enc(b, 0) != "RIFF") throw new Exception("not RIFF");
        int pos = 12, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
        while (pos + 8 <= b.Length)
        {
            string id = Enc(b, pos);
            int sz = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
            else if (id == "data") { dataOff = pos + 8; dataLen = sz; }
            pos += 8 + sz + (sz % 2);
        }
        if (dataOff < 0 || bits != 16) throw new Exception("need 16-bit PCM with data chunk");
        if (dataOff + dataLen > b.Length) dataLen = b.Length - dataOff;
        int frames = dataLen / (2 * ch);
        var s = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            double sum = 0;
            for (int c = 0; c < ch; c++) sum += BitConverter.ToInt16(b, dataOff + (i * ch + c) * 2);
            s[i] = sum / ch / 32768.0;
        }
        return new Wav { S = s, Rate = rate, Ch = ch };
    }

    static string Enc(byte[] b, int o) { return "" + (char)b[o] + (char)b[o + 1] + (char)b[o + 2] + (char)b[o + 3]; }

    // 제자리 radix-2 FFT.
    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { var t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, c2 = i + k + len / 2;
                    double xr = re[c2] * cr - im[c2] * ci;
                    double xi = re[c2] * ci + im[c2] * cr;
                    re[c2] = re[a] - xr; im[c2] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    static double Db(double lin) { return lin <= 1e-12 ? -999 : 20 * Math.Log10(lin); }

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        var files = new List<string>(Directory.GetFiles(dir, "*.wav"));
        files.Sort(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("file,sec,bass%,mid%,high%,flatness,peakProm_dB,peakHz,stutter,envSlope_dB,startClick,endClick");
        foreach (string f in files)
        {
            Wav w;
            try { w = Read(f); } catch (Exception e) { Console.WriteLine(Path.GetFileName(f) + ",ERR," + e.Message); continue; }
            double[] s = w.S; int n = s.Length, rate = w.Rate;

            // ---- 평균 크기 스펙트럼 (1024 해닝, 50% 홉) ----
            const int N = 1024;
            var mag = new double[N / 2];
            int frames = 0;
            var re = new double[N]; var im = new double[N];
            for (int off = 0; off + N <= n; off += N / 2)
            {
                for (int i = 0; i < N; i++)
                {
                    double win = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));
                    re[i] = s[off + i] * win; im[i] = 0;
                }
                Fft(re, im);
                for (int k = 0; k < N / 2; k++) mag[k] += Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                frames++;
            }
            if (frames == 0) { Console.WriteLine(Path.GetFileName(f) + ",ERR,too short"); continue; }
            for (int k = 0; k < N / 2; k++) mag[k] /= frames;

            double binHz = (double)rate / N;
            double eBass = 0, eMid = 0, eHigh = 0, eTot = 0;
            for (int k = 1; k < N / 2; k++)
            {
                double hz = k * binHz, e = mag[k] * mag[k];
                eTot += e;
                if (hz < 150) eBass += e; else if (hz < 6000) eMid += e; else eHigh += e;
            }

            // 스펙트럼 평탄도: 1 에 가까울수록 잡음, 0 에 가까울수록 톤.
            double logSum = 0, arith = 0; int cnt = 0;
            for (int k = 1; k < N / 2; k++) { double m = Math.Max(mag[k], 1e-12); logSum += Math.Log(m); arith += m; cnt++; }
            double flatness = Math.Exp(logSum / cnt) / (arith / cnt);

            // 협대역 피크 돌출도: 각 빈이 ±40빈 이동중앙값 대비 몇 dB 위인가.
            double maxProm = -999, promHz = 0;
            var sorted = new double[81];
            for (int k = 45; k < N / 2 - 45; k++)
            {
                for (int j = 0; j < 81; j++) sorted[j] = mag[k - 40 + j];
                Array.Sort(sorted);
                double med = sorted[40];
                double prom = Db(mag[k]) - Db(med);
                if (prom > maxProm) { maxProm = prom; promHz = k * binHz; }
            }

            // ---- 엔벨로프 (5ms RMS) ----
            int hop = Math.Max(1, rate / 200);
            int eN = n / hop;
            var env = new double[eN];
            for (int i = 0; i < eN; i++)
            {
                double sq = 0;
                for (int j = 0; j < hop; j++) { double v = s[i * hop + j]; sq += v * v; }
                env[i] = Math.Sqrt(sq / hop);
            }

            // 스터터: 20~500ms 지연 구간 엔벨로프 자기상관 최댓값.
            double meanE = 0; for (int i = 0; i < eN; i++) meanE += env[i]; meanE /= Math.Max(eN, 1);
            double var0 = 0; for (int i = 0; i < eN; i++) { double d = env[i] - meanE; var0 += d * d; }
            double stutter = 0;
            int lagMin = eN > 4 ? 4 : 1, lagMax = Math.Min(100, eN - 2);
            for (int lag = lagMin; lag <= lagMax; lag++)
            {
                double acc = 0;
                for (int i = 0; i + lag < eN; i++) acc += (env[i] - meanE) * (env[i + lag] - meanE);
                double r = var0 > 0 ? acc / var0 : 0;
                if (r > stutter) stutter = r;
            }

            // 엔벨로프 기울기: 뒤 1/3 평균 - 앞 1/3 평균 (dB). Eye 큐의 "긴장 상승" 확인용.
            double a1 = 0, a3 = 0; int t = Math.Max(1, eN / 3);
            for (int i = 0; i < t; i++) a1 += env[i];
            for (int i = eN - t; i < eN; i++) a3 += env[i];
            double slope = Db(a3 / t) - Db(a1 / t);

            // 클릭: 첫/끝 샘플이 무음에서 얼마나 떨어져 시작·종료하는가.
            double startClick = Math.Abs(s[0]), endClick = Math.Abs(s[n - 1]);

            Console.WriteLine(string.Format(
                "{0},{1:F2},{2:F1},{3:F1},{4:F1},{5:F3},{6:F1},{7:F0},{8:F2},{9:F1},{10:F1},{11:F1}",
                Path.GetFileName(f), (double)n / rate,
                100 * eBass / eTot, 100 * eMid / eTot, 100 * eHigh / eTot,
                flatness, maxProm, promHz, stutter, slope, Db(startClick), Db(endClick)));
        }
    }
}
