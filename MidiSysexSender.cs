using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Library
{
    public class MidiSysexSender : IDisposable
    {
        private MidiOut? midiOut;
        public List<string> DeviceNames { get; } = new();

        public MidiSysexSender()
        {
            // 使用可能なMIDI出力デバイスを列挙
            for (int i = 0; i < MidiOut.NumberOfDevices; i++)
                DeviceNames.Add(MidiOut.DeviceInfo(i).ProductName);
        }

        /// <summary>
        /// 指定したデバイス番号でMIDI出力を開く
        /// </summary>
        public bool Open(int deviceIndex)
        {
            Close();
            if (deviceIndex >= 0 && deviceIndex < MidiOut.NumberOfDevices)
            {
                midiOut = new MidiOut(deviceIndex);
                return true;
            }
            return false;
        }

        /// <summary>
        /// SysExデータを送信
        /// </summary>
        public void SendSysEx(byte[] sysexData)
        {
            if (midiOut == null)
                throw new InvalidOperationException("MIDI出力が開かれていません。");

            if (sysexData.Length < 2 || sysexData[0] != 0xF0 || sysexData[^1] != 0xF7)
                throw new ArgumentException("SysExデータは 0xF0 で始まり 0xF7 で終わる必要があります。");

            midiOut.SendBuffer(sysexData);
        }

        /// <summary>
        /// 出力を閉じる
        /// </summary>
        public void Close()
        {
            midiOut?.Dispose();
            midiOut = null;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
