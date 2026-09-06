// 화면 노이즈 클립을 연출 길이까지 이어 붙이고, 끝나는 순간에 "픽!" 을 놓는다.
//
// 왜 이어 붙이는가: 고른 원본은 본체가 1.20초에서 끝나고 뒤 230ms 가 -70dB 무음인데,
// ScreenNoiseAction 의 duration 은 1.5초다. 그대로 두면 소리 없는 노이즈가 250ms 남는다.
// 노이즈는 정상 신호라 같은 클립의 안정 구간을 등파워 크로스페이드로 이어 붙이면
// 질감을 바꾸지 않고 늘릴 수 있다.
//
// 왜 노이즈 끝에 페이드아웃을 걸지 않는가: ScreenEffectManager.NoiseRoutine 은 duration 에서
// _noiseImage.enabled = false 로 페이드 없이 곧장 끈다. 소리도 같은 프레임에 잘려야 하고,
// 그 불연속은 바로 뒤에 오는 스냅의 어택이 덮는다 — 그게 "픽!" 이다.
using System;
using System.Globalization;
using System.IO;

static class WavSplice
{
    class Wav { public double[][] Ch; public int Rate; public int Frames; }

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
        if (dataOff < 0 || bits != 16) throw new Exception("need 16-bit PCM: " + path);
        if (dataOff + dataLen > b.Length) dataLen = b.Length - dataOff;
        int frames = dataLen / (2 * ch);
        var lanes = new double[ch][];
        for (int c = 0; c < ch; c++) lanes[c] = new double[frames];
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < ch; c++)
                lanes[c][i] = BitConverter.ToInt16(b, dataOff + (i * ch + c) * 2) / 32768.0;
        return new Wav { Ch = lanes, Rate = rate, Frames = frames };
    }

    static void Write(string path, double[][] lanes, int rate)
    {
        int ch = lanes.Length, frames = lanes[0].Length, align = ch * 2, bytes = frames * align;
        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("RIFF".ToCharArray()); bw.Write(36 + bytes);
            bw.Write("WAVE".ToCharArray()); bw.Write("fmt ".ToCharArray());
            bw.Write(16); bw.Write((short)1); bw.Write((short)ch);
            bw.Write(rate); bw.Write(rate * align); bw.Write((short)align); bw.Write((short)16);
            bw.Write("data".ToCharArray()); bw.Write(bytes);
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < ch; c++)
                {
                    double v = lanes[c][i];
                    if (v > 0.999969) v = 0.999969; if (v < -1.0) v = -1.0;
                    bw.Write((short)Math.Round(v * 32767.0));
                }
        }
    }

    static double Rms(double[][] lanes, int from, int count)
    {
        double sq = 0; int n = 0;
        for (int c = 0; c < lanes.Length; c++)
            for (int i = from; i < from + count && i < lanes[c].Length; i++) { sq += lanes[c][i] * lanes[c][i]; n++; }
        return n > 0 ? Math.Sqrt(sq / n) : 0;
    }

    static double Db(double v) { return v <= 1e-12 ? -999 : 20 * Math.Log10(v); }

    /// <summary>피크 대비 -25dB 를 처음 넘는 지점까지의 앞쪽 무음을 걷어낸다(3ms 여유).</summary>
    static int TrimLead(Wav w)
    {
        double peak = 0;
        for (int c = 0; c < w.Ch.Length; c++)
            for (int i = 0; i < w.Frames; i++) peak = Math.Max(peak, Math.Abs(w.Ch[c][i]));
        double thr = peak * Math.Pow(10, -25.0 / 20.0);
        int start = 0;
        while (start < w.Frames)
        {
            double m = 0;
            for (int c = 0; c < w.Ch.Length; c++) m = Math.Max(m, Math.Abs(w.Ch[c][start]));
            if (m >= thr) break;
            start++;
        }
        return Math.Max(0, start - (int)(0.003 * w.Rate));
    }

    /// <summary>가장 큰 window 구간의 RMS. 트랜지언트의 체감 크기는 피크가 아니라 이 값에 가깝다.</summary>
    static double LoudestRms(double[][] lanes, int from, int count, int window)
    {
        double best = 0;
        for (int start = from; start + window <= from + count && start + window <= lanes[0].Length; start += window / 4)
        {
            double r = Rms(lanes, start, window);
            if (r > best) best = r;
        }
        return best;
    }

    /// <summary>tanh 소프트 클립. 전기적 팝에는 약간의 포화가 오히려 자연스럽고 밀도를 올린다.</summary>
    static double SoftClip(double v, double ceiling)
    {
        return ceiling * Math.Tanh(v / ceiling);
    }

    static void Main(string[] args)
    {
        // args: <noiseWav> <snapWav> <outWav> <noiseSeconds> [snapRmsDb] [layerWav] [layerRmsDb]
        string noisePath = args[0], snapPath = args[1], outPath = args[2];
        double noiseSec = double.Parse(args[3], CultureInfo.InvariantCulture);
        // 피크가 아니라 "가장 큰 20ms RMS" 를 맞춘다. 피크 -6dBFS 로 맞췄더니
        // 20ms RMS 가 -23dB 로 앞선 노이즈(-17dB)보다 낮아 픽이 들리지 않았다.
        double snapRmsDb = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : -10.0;
        string layerPath = args.Length > 5 && args[5] != "-" ? args[5] : null;
        double layerRmsDb = args.Length > 6 ? double.Parse(args[6], CultureInfo.InvariantCulture) : -14.0;

        Wav n = Read(noisePath), s = Read(snapPath);
        int rate = n.Rate, ch = n.Ch.Length;
        int target = (int)Math.Round(noiseSec * rate);

        // ── 1) 노이즈를 target 길이까지 확장 ──────────────────────────
        int splice = (int)Math.Round(1.10 * rate);   // 감쇠 시작(1.15초) 직전
        int xfade = (int)Math.Round(0.060 * rate);   // 등파워 크로스페이드 60ms
        int donorFrom = (int)Math.Round(0.35 * rate);
        int donorLen = target - splice + xfade;      // 1.10초부터 끝까지 덮을 분량

        if (donorFrom + donorLen > n.Frames)
            throw new Exception("도너 구간이 원본을 넘습니다. splice/donorFrom 을 조정하세요.");

        // 도너를 접합 지점의 국소 음량에 맞춘다. 원본이 그 지점에서 이미 잦아든 상태라
        // 안정 구간을 그대로 붙이면 끝에서 갑자기 커진다.
        double localRms = Rms(n.Ch, splice - (int)(0.05 * rate), (int)(0.05 * rate));
        double donorRms = Rms(n.Ch, donorFrom, donorLen);
        double match = donorRms > 0 ? localRms / donorRms : 1.0;

        var outLanes = new double[ch][];
        int total = target + s.Frames;             // 스냅 자리는 뒤에서 정확히 다시 계산한다
        for (int c = 0; c < ch; c++) outLanes[c] = new double[total];

        for (int c = 0; c < ch; c++)
        {
            for (int i = 0; i < splice && i < n.Frames; i++) outLanes[c][i] = n.Ch[c][i];

            for (int k = 0; k < donorLen; k++)
            {
                int dst = splice - xfade + k;
                if (dst < 0 || dst >= target) continue;
                double d = n.Ch[c][donorFrom + k] * match;
                if (k < xfade)
                {
                    // 등파워: 무상관 잡음끼리는 sqrt 커브라야 합성 RMS 가 평평하다.
                    double t = (double)k / xfade;
                    double a = Math.Sqrt(1 - t), b = Math.Sqrt(t);
                    outLanes[c][dst] = outLanes[c][dst] * a + d * b;
                }
                else outLanes[c][dst] = d;
            }
        }

        // ── 2) 스냅 다듬기: 앞쪽 무음 제거 후 RMS 정규화 ────────────────
        int sStart = TrimLead(s);
        int sLen = Math.Min(s.Frames - sStart, (int)(0.15 * rate));
        int win = (int)(0.020 * rate);
        double sLoud = LoudestRms(s.Ch, sStart, sLen, win);
        double sGain = sLoud > 0 ? Math.Pow(10, snapRmsDb / 20.0) / sLoud : 1.0;
        int snapFade = (int)(0.010 * rate);

        // 선택 레이어: 밝은 팝 밑에 낮은 툭을 깔아 CRT 차단음의 두 성분을 만든다.
        // ElevenLabs 문서도 복합 효과는 개별 생성 후 편집기에서 합치라고 안내한다.
        Wav L = null; int lStart = 0, lLen = 0; double lGain = 1;
        if (layerPath != null)
        {
            L = Read(layerPath);
            lStart = TrimLead(L);
            lLen = Math.Min(L.Frames - lStart, (int)(0.20 * rate));
            double lLoud = LoudestRms(L.Ch, lStart, lLen, win);
            lGain = lLoud > 0 ? Math.Pow(10, layerRmsDb / 20.0) / lLoud : 1.0;
            sLen = Math.Max(sLen, lLen);
        }

        // ── 3) 정확히 target 프레임에 스냅을 놓는다 ────────────────────
        int outLen = target + sLen;
        var final = new double[ch][];
        for (int c = 0; c < ch; c++)
        {
            final[c] = new double[outLen];
            Array.Copy(outLanes[c], final[c], target);
            for (int i = 0; i < sLen; i++)
            {
                double v = 0;
                int fromEnd = sLen - 1 - i;
                double fade = fromEnd < snapFade ? (double)fromEnd / snapFade : 1.0;

                int sIdx = sStart + i;
                if (sIdx < s.Frames && i < s.Frames - sStart)
                    v += s.Ch[Math.Min(c, s.Ch.Length - 1)][sIdx] * sGain;

                if (L != null && i < lLen && lStart + i < L.Frames)
                    v += L.Ch[Math.Min(c, L.Ch.Length - 1)][lStart + i] * lGain;

                // 하드 클립 대신 tanh 포화 — 팝의 밀도를 올리면서 디지털 왜곡을 피한다.
                final[c][target + i] = SoftClip(v * fade, 0.94);
            }
        }

        Write(outPath, final, rate);

        Console.WriteLine("out            : " + Path.GetFileName(outPath));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "noise          : 0 -> {0:F3}s (원본 {1:F3}s 를 {2:F2}s 지점에서 {3}ms 등파워 크로스페이드로 연장)",
            noiseSec, (double)n.Frames / rate, (double)splice / rate, 1000 * xfade / rate));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "donor match    : 접합점 국소 {0:F1}dB / 도너 {1:F1}dB -> 게인 {2:F2}dB",
            Db(localRms), Db(donorRms), Db(match)));
        // 실제로 붙은 결과를 되재서 노이즈 대비 얼마나 튀는지 보여준다.
        double noiseTail = Rms(final, target - (int)(0.05 * rate), (int)(0.05 * rate));
        double snapLoud = LoudestRms(final, target, sLen, win);
        double outPeak = 0;
        for (int c = 0; c < ch; c++)
            for (int i = 0; i < outLen; i++) outPeak = Math.Max(outPeak, Math.Abs(final[c][i]));

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "snap           : {0:F3}s 에 배치, 앞 {1:F1}ms 잘라내고 {2:F0}ms 사용{3}",
            noiseSec, 1000.0 * sStart / rate, 1000.0 * sLen / rate,
            layerPath != null ? " (+저역 레이어)" : ""));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "loudness       : 직전 노이즈 {0:F1}dB / 스냅 20ms RMS {1:F1}dB -> 차이 {2:+0.0;-0.0}dB",
            Db(noiseTail), Db(snapLoud), Db(snapLoud) - Db(noiseTail)));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "peak           : {0:F2}dBFS", Db(outPeak)));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "total          : {0:F3}s", (double)outLen / rate));
    }
}
