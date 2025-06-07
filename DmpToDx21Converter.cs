using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace KeyboardSamplerWPF
{
    public class DmpToDx21ConverterStrict
    {
        private readonly Voice voice = new();
        private readonly Dx21FunctionParams functionParams = new();

        private byte[] vced = { 0 };

        // .dmpファイルの読み込み(Deflemask形式)
        public void LoadDmpFile(string path)
        {

            var data = File.ReadAllBytes(path);
            if (data.Length < 51)
                throw new InvalidDataException("DMPファイルが短すぎます。");

            int index = 0;
            // 1. ファイルバージョンチェック
            byte fileVersion = data[index++];
            if (fileVersion != 0x0B)
                throw new InvalidDataException("サポート外のDMPファイルバージョンです。");

            // 2. システムチェック
            voice.SystemCode = data[index++]; // SYSTEMコードを保存

            if (voice.SystemCode != 0x02 && voice.SystemCode != 0x08 && voice.SystemCode != 0x07)
                throw new InvalidDataException("SYSTEM_GENESIS、YM2151、YM2203以外には対応していません。");

            // 3. FMモードチェック
            byte instrumentMode = data[index++];
            if (instrumentMode != 0x01)
                throw new InvalidDataException("FMモードのDMPファイルのみ対応しています。");

            // 音色データを初期化
            voice.Reset(); 

            // 4. LFO (PMS) 読み飛ばし
            voice.PMS = data[index++];      //YM2612はFMS　YM2203は非対応

            // 5. フィードバックとアルゴリズム取得
            voice.Feedback = data[index++];
            voice.Algorithm = data[index++];

            // 6. LFO2 (AMS) 読み飛ばし
            voice.AMS = data[index++];

            // 7. オペレータデータ読み込み (4オペレータ)
            voice.Operators.Clear();
            int[] operatorOrder = { 0, 2, 1, 3 };

            for (int opNum = 0; opNum < 4; opNum++)
            {
                int baseIndex = index + operatorOrder[opNum] * 11; 

                byte mul = data[baseIndex++];
                byte tl = data[baseIndex++];
                byte ar = data[baseIndex++];
                byte dr = data[baseIndex++];
                byte sl = data[baseIndex++];
                byte rr = data[baseIndex++];
                byte am = data[baseIndex++];
                byte rs = data[baseIndex++];
                byte dtPacked = data[baseIndex++];  // ← ここにDTとDT2が入っている
                byte d2r = data[baseIndex++];
                byte ssgeg = data[baseIndex++];

                voice.Operators.Add(new Voice.OperatorParams
                {
                    MUL = mul,
                    TL = tl,
                    AR = ar,
                    DR = dr,
                    SL = sl,
                    RR = rr,
                    AM = am,
                    RS = rs,
                    DT = (byte)(dtPacked & 0x0F),      // 下位4ビットがDT
                    DT2 = (byte)((dtPacked >> 4) & 0x0F), // 上位4ビットがDT2
                    D2R = d2r,
                    SSGEG = (byte)(ssgeg & 0x07) // SSGEG_Enable (bit3) | SSGEG_Type (bit0-2)
                });
            }
        }

        public void SetVoiceName(string name)
        {
            voice.VoiceName = (name.Length > 10) ? name.Substring(0, 10) : name; // DX21は10文字
        }

        public void SaveAsSyx(string path)
        {
            var sysex = BuildSingleVoiceVCEDSysEx();
            File.WriteAllBytes(path, sysex);
        }

        public byte[] BuildSingleVoiceVCEDSysEx()
        {

            if (voice.Operators.Count != 4)
                throw new InvalidOperationException($"オペレータ数が4ではありません（{voice.Operators.Count}）。変換できません。");


            vced = new byte[93]; 

            int[] operatorOrder = { 0, 2, 1, 3 };

            for (int opNum = 0; opNum < 4; opNum++)
            {
                var op = voice.Operators[operatorOrder[opNum]];
                int baseIndex = opNum * 13;

                vced[baseIndex + 0] = op.AR;
                vced[baseIndex + 1] = op.DR;
                vced[baseIndex + 2] = op.D2R;
                vced[baseIndex + 3] = op.RR;
                vced[baseIndex + 4] = (byte)(15 - op.SL); // YM2612 → DX21 に変換                 
                vced[baseIndex + 5] = 0; // Level Scaling (not present in DMP)
                vced[baseIndex + 6] = op.RS; // fix ver1.01
                vced[baseIndex + 7] = 0; // EG Bias Sensitivity (not used)
                vced[baseIndex + 8] = op.AM; // Amplitude Mod Sensitivity (0 or 1)
                vced[baseIndex + 9] = 0; // Key Velocity Sensitivity (not used)
                vced[baseIndex + 10] = ConvertYM2612TLtoDx21OutputFix(op.TL); 
                vced[baseIndex + 11] = GetDX21Freq(op.MUL, op.DT2); // ← 修正済み
                vced[baseIndex + 12] = ConvertDetune(op.DT);
            }

            // 共通パラメータ
            vced[52] = (byte)Math.Clamp((int)voice.Algorithm, 0, 7);         // Algorithm: 0〜7
            vced[53] = (byte)Math.Clamp((int)voice.Feedback, 0, 7);          // Feedback: 0〜7
            vced[54] = (byte)Math.Clamp((int)voice.LfoSpeed, 0, 99);         // LFO Speed: 0〜99
            vced[55] = (byte)Math.Clamp((int)voice.LfoDelay, 0, 99);         // LFO Delay: 0〜99
            vced[56] = (byte)Math.Clamp((int)voice.PitchModDepth, 0, 99);    // Pitch Mod Depth: 0〜99
            vced[57] = (byte)Math.Clamp((int)voice.AmpModDepth, 0, 99);      // Amp Mod Depth: 0〜99
            vced[58] = (byte)(voice.LfoSync != 0 ? 1 : 0);                    // LFO Sync: 0 or 1
            vced[59] = (byte)Math.Clamp((int)voice.LfoWaveform, 0, 3);       // LFO Waveform: 0〜3
            vced[60] = (byte)Math.Clamp((int)voice.PMS, 0, 7);               // Pitch Mod Sensitivity: 0〜7
            vced[61] = (byte)Math.Clamp((int)voice.AMS, 0, 3);               // Amp Mod Sensitivity: 0〜3

            // パフォーマンス
            vced[62] = (byte)Math.Clamp((int)functionParams.Transpose, 0, 48);              // VCED[62]：トランスポーズ（0=C1 ～ 48=C5）
            vced[63] = (byte)(functionParams.PlayMode != 0 ? 1 : 0);                        // VCED[63]：発音モード（0=Poly、1=Mono）
            vced[64] = (byte)Math.Clamp((int)functionParams.PitchBendRange, 0, 12);         // VCED[64]：ピッチベンドレンジ（0～12）
            vced[65] = (byte)(functionParams.PortamentoMode != 0 ? 1 : 0);                  // VCED[65]：ポルタメントモード（0=OFF、1=ON）
            vced[66] = (byte)Math.Clamp((int)functionParams.PortamentoTime, 0, 99);         // VCED[66]：ポルタメント時間（0～99）
            vced[67] = (byte)Math.Clamp((int)functionParams.FootVolume, 0, 99);             // VCED[67]：フットボリューム（0～99）
            vced[68] = (byte)(functionParams.SustainPedal != 0 ? 1 : 0);                    // VCED[68]：サステインペダル（0=OFF、1=ON）
            vced[69] = (byte)(functionParams.PortaFootSwitch != 0 ? 1 : 0);                 // VCED[69]：ポルタメントフットスイッチ（0=OFF、1=ON）
            vced[70] = (byte)(functionParams.ChorusEnable != 0 ? 1 : 0);                    // VCED[70]：コーラス（0=OFF、1=ON）
            vced[71] = (byte)Math.Clamp((int)functionParams.ModWheelPitchRange, 0, 99);     // VCED[71]：MWピッチ変調レンジ（0～99）
            vced[72] = (byte)Math.Clamp((int)functionParams.ModWheelAmpRange, 0, 99);       // VCED[72]：MW音量変調レンジ（0～99）

            // VCED[73]〜[76]：未使用領域（初期化）
            for (int i = 73; i <= 76; i++)
                vced[i] = 0x00;

            // 音色名：77〜86（10バイトのみ書き込み）
            var nameBytes = Encoding.ASCII.GetBytes(voice.VoiceName.PadRight(10));
            Array.Copy(nameBytes, 0, vced, 77, 10);

            // Pitch EG（87〜89）
            for (int i = 87; i < 90; i++) vced[i] = 0x63; // Rate = 99（即座に完了）
            for (int i = 90; i < 93; i++) vced[i] = 0x32; // Level = 50（センター）

            // SysEx組み立て（受信用 1音色フォーマット）
            List<byte> sysex =
    [
        0xF0, 0x43, 0x00, 0x03, 0x00, 0x5D, .. vced // Yamaha DX21 1Voice VCED Header
    ];

            byte checksum = CalculateChecksum(vced);
            sysex.Add(checksum);
            sysex.Add(0xF7); // SysEx End

            return sysex.ToArray();
        }




        public void SetVoiceCommonParams(
            int lfoSpeed,
            int lfoDelay,
            int pitchModSens,
            int ampModSens,
            int lfoSync,
            int lfoWaveforms,
            int pms,
            int ams)
        {
            voice.LfoSpeed = (byte)Math.Clamp(lfoSpeed, 0, 99);
            voice.LfoDelay = (byte)Math.Clamp(lfoDelay, 0, 99);
            voice.PitchModDepth = (byte)Math.Clamp(pitchModSens, 0, 99);
            voice.AmpModDepth = (byte)Math.Clamp(ampModSens, 0, 99);
            voice.LfoSync = (byte)(lfoSync != 0 ? 1 : 0);               // ON/OFF
            voice.LfoWaveform = (byte)Math.Clamp(lfoWaveforms, 0, 3);    // 0=Triangle, 1=SawUp, 2=Square, 3=Sample/Hold
            voice.PMS = (byte)Math.Clamp(pms, 0, 7);
            voice.AMS = (byte)Math.Clamp(ams, 0, 3);
        }



        public void SetFunctionParams(
            int transpose,
            int playMode,
            int pitchBend,
            int sustain,
            int chorus,
            int portaMode,
            int portaTime,
            int mwPitch,
            int mwAmp,
            int footVolume,
            int portaFootSwitch)
        {
            functionParams.Transpose = (byte)Math.Clamp(transpose, 0, 48);
            functionParams.PlayMode = (byte)(playMode == 1 ? 1 : 0);
            functionParams.PitchBendRange = (byte)Math.Clamp(pitchBend, 0, 12);
            functionParams.PortamentoMode = (byte)(portaMode != 0 ? 1 : 0);
            functionParams.PortamentoTime = (byte)Math.Clamp(portaTime, 0, 99);
            functionParams.SustainPedal = (byte)(sustain != 0 ? 1 : 0);
            functionParams.ChorusEnable = (byte)(chorus != 0 ? 1 : 0);
            functionParams.ModWheelPitchRange = (byte)Math.Clamp(mwPitch, 0, 99);
            functionParams.ModWheelAmpRange = (byte)Math.Clamp(mwAmp, 0, 99);
            functionParams.FootVolume = (byte)Math.Clamp(footVolume, 0, 99);
            functionParams.PortaFootSwitch = (byte)(portaFootSwitch != 0 ? 1 : 0);
        }
       
        public int GetOperatorCount()
        {
            return voice.Operators.Count;
        }

        ///
        /// データ変換用関数
        ///


        // YM2612のTL値（0 = 最大音量, 127 = 無音）を、DX21のOutput Level（0 = 無音, 99 = 最大音量）に変換する
        private static byte ConvertYM2612TLtoDx21Output(byte tl)
        {
            return (byte)Math.Clamp(99 - (tl * 99 / 127), 0, 99);
        }

        private static byte ConvertYM2612TLtoDx21OutputFix(byte tl)
        {
            // 99を上限とし、それを基準に反転（DX21の最大値 99 → 無音、0 → 最大音量）
            int clamped = Math.Min((int)tl, 99);
            return (byte)(99 - clamped);
        }


        // Freq Ratio → Freq値を近似検索（変換表に基づく）
        private static byte LookupFreqIndex(double ratio)
        {
            double[] freqRatios = new double[]
            {
        0.50, 0.71, 0.78, 0.87, 1.00, 1.41, 1.57, 1.73,
        2.00, 2.82, 3.00, 3.14, 3.46, 4.00, 4.24, 4.71,
        5.00, 5.19, 5.65, 6.00, 6.28, 6.92, 7.00, 7.07,
        7.85, 8.00, 8.48, 8.65, 9.00, 9.42, 9.89, 10.00,
        10.38, 10.99, 11.30, 11.55, 12.00, 12.11, 12.56, 12.72,
        13.00, 13.84, 14.00, 14.10, 14.13, 15.00, 15.55, 15.57,
        15.70, 16.96, 17.27, 17.30, 18.37, 18.84, 19.00, 19.78,
        20.41, 20.76, 21.20, 21.98, 22.49, 23.55, 24.22, 25.95
            };

            double minDiff = double.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < freqRatios.Length; i++)
            {
                double diff = Math.Abs(freqRatios[i] - ratio);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestIndex = i;
                }
            }
            return (byte)bestIndex;
        }

        // YM2151 → DX21 マルチプル変換
        private static float GetFrequencyRatio(byte mul, byte dt2)
        {
            float[] dt2Table = { 1.00f, 1.41f, 1.57f, 1.73f };
            if (dt2 > 3) dt2 = 0;
            float ratio = (mul == 0) ? 0.5f * dt2Table[dt2] : mul * dt2Table[dt2];
            return ratio;
        }

        // YM2151 → DX21 ConvertDetune
        private static byte ConvertDetune(byte dt)
        {
            return dt switch
            {
                0 => 0,  // -3 => ±0
                1 => 1,  // -2 => +1
                2 => 2,  // -1 => +2
                3 => 3,  // 0 => +3
                4 => 4,  // 1 => -1
                5 => 5,  // 2 => -2
                6 => 6,  // 3 => -3
                _ => 0,  // 念のため（範囲外安全策）fix
            };
        }


        private static byte GetDX21Freq(byte mul, byte dt2)
        {
            float ratio = GetFrequencyRatio(mul, dt2);
            return LookupFreqIndex(ratio);
        }

        public void ShiftOctave(int steps)
        {
            if (steps == 0) return;

            float factor = (float)Math.Pow(2, steps); // 2^steps で倍率を計算

            foreach (var op in voice.Operators)
            {
                // 現在の実効周波数倍率（MUL × DT2補正）
                float currentRatio = GetFrequencyRatio(op.MUL, op.DT2);

                // オクターブシフト適用
                float shiftedRatio = currentRatio * factor;

                // 新しいMULとDT2を近似的に逆算
                ApproximateMulDt2FromFreqRatio(shiftedRatio, out byte newMul, out byte newDt2);

                // オペレータに反映
                op.MUL = newMul;
                op.DT2 = newDt2;
                op.DT = 0;
            }
        }


        private static void ApproximateMulDt2FromFreqRatio(float ratio, out byte mul, out byte dt2)
        {
            // 候補表
            float[] dt2Table = { 1.00f, 1.41f, 1.57f, 1.73f };

            float minDiff = float.MaxValue;
            byte bestMul = 1;
            byte bestDt2 = 0;

            for (byte m = 0; m <= 15; m++)
            {
                for (byte d = 0; d < dt2Table.Length; d++)
                {
                    float r = (m == 0) ? 0.5f * dt2Table[d] : m * dt2Table[d];
                    float diff = Math.Abs(r - ratio);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        bestMul = m;
                        bestDt2 = d;
                    }
                }
            }

            mul = bestMul;
            dt2 = bestDt2;
        }

        // / チェックサム計算
        private static byte CalculateChecksum(byte[] vced)
        {
            int sum = 0;
            for (int i = 0; i < vced.Length; i++)
                sum += vced[i];

            int remainder = sum % 128;
            int checksum = (128 - remainder) & 0x7F;
            return (byte)checksum;
        }


        ///
        /// テキスト変換
        ///

        public string GetOperatorDataAsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Algorithm: {voice.Algorithm}, Feedback: {voice.Feedback}, PMS: {voice.PMS}, AMS: {voice.AMS}");
 
            // フィールド名ヘッダー
            sb.AppendLine("OP  MUL  TL  AR  DR  SL  RR  AM  RS  DT DT2 D2R SSG");

            // 各オペレータ行
            for (int i = 0; i < 4; i++)
            {
                var op = voice.Operators[i];
                sb.AppendLine(
                    $"{(i + 1),2}  {op.MUL,3} {op.TL,3} {op.AR,3} {op.DR,3} {op.SL,3} {op.RR,3}" +
                    $" {op.AM,3} {op.RS,3} {op.DT - 3,3} {op.DT2,3} {op.D2R,3} {op.SSGEG,3}"
                );
            }

            return sb.ToString();
        }



        public string GetDx21VCEDSyxDataAsText()
        {
            if (vced == null)
                BuildSingleVoiceVCEDSysEx(); // vced を構築

            var sb = new StringBuilder();
            ReadOnlySpan<byte> vcedSpan;

            if (vced.Length == 101 && vced[0] == 0xF0 && vced[5] == 0x5D)
            {
                vcedSpan = vced.AsSpan(6, 93);
            }
            else if (vced.Length == 93)
            {
                vcedSpan = vced.AsSpan();
            }
            else
            {
                return "[VCED 読み取り失敗] 不正なデータ長です。";
            }

            sb.AppendLine($"Algorithm: {vcedSpan[52]}, Feedback: {vcedSpan[53]}");

            // フィールド名ヘッダー
            sb.AppendLine("OP  AR  D1  D2  RR  D1L LS  RS  BS  AM  KV  OL FRQ  DT");

            // オペレータ順 DX21: OP4→2→3→1
            int[] operatorOrder = { 3, 1, 2, 0 };
            for (int opNum = 0; opNum < 4; opNum++)
            {
                int op = operatorOrder[opNum];
                int baseIndex = op * 13;

                sb.AppendLine(
                    $"{opNum + 1,2} " +
                    $"{vcedSpan[baseIndex + 0],3} " +
                    $"{vcedSpan[baseIndex + 1],3} " +
                    $"{vcedSpan[baseIndex + 2],3} " +
                    $"{vcedSpan[baseIndex + 3],3} " +
                    $"{vcedSpan[baseIndex + 4],3} " +
                    $"{vcedSpan[baseIndex + 5],3} " +
                    $"{vcedSpan[baseIndex + 6],3} " +
                    $"{vcedSpan[baseIndex + 7],3} " +
                    $"{vcedSpan[baseIndex + 8],3} " +
                    $"{vcedSpan[baseIndex + 9],3} " +
                    $"{vcedSpan[baseIndex + 10],3} " +
                    $"{vcedSpan[baseIndex + 11],3} " +
                    $"{vcedSpan[baseIndex + 12],3}"
                );
            }

            // ボイス名とピッチEG情報
            string name = Encoding.ASCII.GetString(vcedSpan.Slice(77, 10)).Trim();
            sb.AppendLine($"Voice Name: \"{name}\"");
            sb.AppendLine($"Pitch EG Rates:   {vcedSpan[87]}, {vcedSpan[88]}, {vcedSpan[89]}");
            sb.AppendLine($"Pitch EG Levels:  {vcedSpan[90]}, {vcedSpan[91]}, {vcedSpan[92]}");

            return sb.ToString();
        }

        public void MaximizeCarrierVolumeByOffset()
        {
            int[][] carrierMap = new int[][]
            {
    new[] { 3 },             // ALG 0: OP4のみが出力（OP1←2←3←4）
    new[] { 3 },             // ALG 1: OP4のみ（(OP1+OP2)←3←4）
    new[] { 3 },             // ALG 2: OP4のみ（(1←2)+(3←4)）
    new[] { 3 },             // ALG 3: OP4のみ（全加算 1+2+3+4←OP4）
    new[] { 1, 3 },          // ALG 4: OP2, OP4が出力
    new[] { 1, 2, 3 },       // ALG 5: OP2, OP3, OP4が出力
    new[] { 1, 2, 3 },       // ALG 6: OP2, OP3, OP4が出力
    new[] { 0, 1, 2, 3 }     // ALG 7: すべてのOPが出力（独立）
            };

            if (voice.Algorithm < 0 || voice.Algorithm >= carrierMap.Length)
                return;

            var carriers = carrierMap[voice.Algorithm];

            // 最も音が大きい（TLが小さい）キャリアに合わせて全体を持ち上げる
            int minTL = carriers.Select(i => voice.Operators[i].TL).Min();

            foreach (int i in carriers)
            {
                voice.Operators[i].TL = (byte)Math.Max(0, voice.Operators[i].TL - minTL);
            }
        }



    }


    ///
    /// 音色データ保管用クラス
    ///

    public class Voice
    {
        public class OperatorParams
        {
            public byte MUL;
            public byte TL;
            public byte AR;
            public byte DR;
            public byte SL;
            public byte RR;
            public byte AM;
            public byte RS;
            public byte DT;
            public byte DT2;
            public byte D2R;
            public byte SSGEG;
        }

        public List<OperatorParams> Operators { get; } = new();

        public string VoiceName { get; set; } = "";
        public byte SystemCode { get; set; }

        // VCED共通パラメータ（54〜61）
        public byte Algorithm { get; set; }
        public byte Feedback { get; set; }
        public byte LfoSpeed { get; set; }          // VCED[54] LFO Speed
        public byte LfoDelay { get; set; }          // VCED[55] LFO Delay
        public byte PitchModDepth{ get; set; }     // VCED[56] Pitch Mod Depth
        public byte AmpModDepth { get; set; }       // VCED[57] Amp Mod Depth
        public byte LfoSync { get; set; }           // VCED[58] LFO Sync
        public byte LfoWaveform { get; set; }       // VCED[59] LFO Waveform
        public byte PMS { get; set; }               // VCED[60] Pitch Mod Sensitivity PMS（Pitch Modulation Sensitivity)
        public byte AMS { get; set; }               // VCED[61] Amp Mod Sensitivity AMS（Amplitude Modulation Sensitivity）

        public void Reset()
        {
            Operators.Clear();
            for (int i = 0; i < 4; i++)
            {
                Operators.Add(new OperatorParams
                {
                    MUL = 1,
                    TL = 0,
                    AR = 0,
                    DR = 0,
                    SL = 0,
                    RR = 0,
                    AM = 0,
                    RS = 0,
                    DT = 3,    // DT = 3: ±0
                    DT2 = 0,
                    D2R = 0,
                    SSGEG = 0
                });
            }

            Algorithm = 0;
            Feedback = 0;
            VoiceName = "";
            SystemCode = 0;

            LfoSpeed = 0;
            LfoDelay = 0;
            PitchModDepth = 0;
            AmpModDepth = 0;
            LfoSync = 0;
            LfoWaveform = 0;
            PMS = 0;
            AMS = 0;
        }

    }

    public class Dx21FunctionParams
    {
        public byte Transpose { get; set; } = 24;          // VCED[62]：トランスポーズ（0=C1 ～ 48=C5、C3=24）
        public byte PlayMode { get; set; } = 0;            // VCED[63]：発音モード（0=POLY、1=MONO）
        public byte PitchBendRange { get; set; } = 2;      // VCED[64]：ピッチベンドレンジ（0～12）
        public byte PortamentoMode { get; set; } = 0;      // VCED[65]：ポルタメントモード（0=OFF、1=ON）
        public byte PortamentoTime { get; set; } = 0;      // VCED[66]：ポルタメント時間（0～99）
        public byte FootVolume { get; set; } = 0;          // VCED[67]：フットボリューム（0～99）
        public byte SustainPedal { get; set; } = 1;        // VCED[68]：サステインペダル（0=OFF、1=ON）
        public byte PortaFootSwitch { get; set; } = 0;     // VCED[69]：ポルタメントフットスイッチ（0=OFF、1=ON）
        public byte ChorusEnable { get; set; } = 0;        // VCED[70]：コーラス（0=OFF、1=ON）
        public byte ModWheelPitchRange { get; set; } = 0;  // VCED[71]：MWによるピッチ変調レンジ（0～99）
        public byte ModWheelAmpRange { get; set; } = 0;    // VCED[72]：MWによる音量変調レンジ（0～99）

        public void Reset()
        {
            Transpose = 24;
            PlayMode = 0;
            PitchBendRange = 2;
            PortamentoMode = 0;
            PortamentoTime = 0;
            FootVolume = 0;
            SustainPedal = 1;
            PortaFootSwitch = 0;
            ChorusEnable = 0;
            ModWheelPitchRange = 0;
            ModWheelAmpRange = 0;
        }
    }


}
