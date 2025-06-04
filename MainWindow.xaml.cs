using KeyboardSamplerWPF;
using Library;
using Microsoft.Win32;
using NAudio.Midi;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace DmpToDx21
{
    public partial class MainWindow : Window
    {
        private readonly DmpToDx21ConverterStrict converter = new();
        private string? loadedFileNameWithoutExt;

        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < MidiOut.NumberOfDevices; i++)
            {
                var info = MidiOut.DeviceInfo(i);
                comboMidiOut.Items.Add(new ComboBoxItem { Content = info.ProductName, Tag = i });
            }

            if (comboMidiOut.Items.Count > 0)
                comboMidiOut.SelectedIndex = comboMidiOut.Items.Count -1;
        }

        private void btnLoadDmp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "DMP files (*.dmp)|*.dmp|All files (*.*)|*.*",
                Title = "DMPファイルを選択してください"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // DMP ファイル読み込み
                    converter.LoadDmpFile(dialog.FileName);

                    // 音量最大化がチェックされていたら編集
                    if (checkMaximizeVolume.IsChecked == true)
                    {
                        converter.MaximizeCarrierVolumeByOffset(); 

                        // ファイル名に "_max" を追加
                        string baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
                        loadedFileNameWithoutExt = baseName + "_max";
                    }
                    else
                    {
                        loadedFileNameWithoutExt = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }

                    // テキスト出力と音色名設定
                    txtInput.Text = converter.GetOperatorDataAsText();
                    string name = loadedFileNameWithoutExt;
                    txtVoiceName.Text = name.Length > 10 ? name.Substring(0, 10) : name;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("読み込みエラー: " + ex.Message);
                }
            }
        }


        private void btnConvertVCED_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (converter == null || converter.GetOperatorCount() != 4)
                {
                    MessageBox.Show("音色データが不完全です。DMPファイルの内容を確認してください。");
                    return;
                }

                converter.ShiftOctave(GetSelectedOctaveShift());
                converter.SetVoiceName(txtVoiceName.Text.Trim());

                // VCED[54]～[59] に相当する LFO・モジュレーション関連パラメータを voice に設定
                converter.SetVoiceCommonParams(
                    GetComboValue(comboLfoSpeed),           // VCED[54]：LFO Speed（0〜99）
                    GetComboValue(comboLfoDelay),           // VCED[55]：LFO Delay（0〜99）
                    GetComboValue(comboPMD),                // VCED[56]：Pitch Mod Depth（ピッチ変調の深さ）
                    GetComboValue(comboAMD),                // VCED[57]：Amp Mod Depth（音量変調の深さ）
                    GetComboTagValue(comboLfoSync),         // VCED[58]：LFO Sync（0=ON, 1=OFF）
                    GetComboTagValue(comboLfoWave),         // VCED[59]：LFO Waveform（0=Triangle, 1=SawUp, 2=Square, 3=S/H）
                    GetComboValue(comboPitchModSens),       // VCED[56]：Pitch Mod Depth（ピッチ変調の深さ）
                    GetComboValue(comboAmpModSens)          // VCED[57]：Amp Mod Depth（音量変調の深さ）
                );

                // VCED[62]～[72] に相当するパフォーマンスパラメータを functionParams に設定
                converter.SetFunctionParams(
                    GetComboTagValue(comboTranspose, 24),         // VCED[62]：Transpose（0=C1 ～ 48=C5）
                    comboPolyMode.SelectedIndex == 1 ? 1 : 0,     // VCED[63]：Play Mode（0=POLY, 1=MONO）
                    GetComboValue(comboPitchBendRange),           // VCED[64]：Pitch Bend Range（0～12）
                    GetComboTagValue(comboSustain),               // VCED[68]：Sustain Pedal（0=OFF, 1=ON）
                    GetComboTagValue(comboChorus),                // VCED[70]：Chorus Enable（0=OFF, 1=ON）
                    GetComboTagValue(comboPortaMode),             // VCED[65]：Portamento Mode（0=OFF, 1=ON）
                    GetComboValue(comboPortaTime),                // VCED[66]：Portamento Time（0～99）
                    GetComboValue(comboWheelPitch),               // VCED[71]：MW Pitch Mod Range（0～99）
                    GetComboValue(comboWheelAmp),                 // VCED[72]：MW Amp Mod Range（0～99）
                    GetComboValue(comboFootVolume),               // VCED[67]：Foot Volume（0～99）
                    GetComboTagValue(comboPortaFootSwitch)        // VCED[69]：Porta Foot Switch（0=OFF, 1=ON）
                );


                var syx = converter.BuildSingleVoiceVCEDSysEx();
                if (syx == null)
                {
                    MessageBox.Show("SysExデータの生成に失敗しました。");
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Filter = "DX21 SysEx (*.syx)|*.syx",
                    FileName = (loadedFileNameWithoutExt ?? "vced") + "_vced.syx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveDialog.FileName, syx);
                    txtStatus.Text = "保存完了";
                }

                txtOutput.Text = converter.GetDx21VCEDSyxDataAsText();

                txtBinaryEditor.Text = HexFormatter.FormatHexDumpWithAsciiAndHeader(syx);
            }
            catch (Exception ex)
            {
                MessageBox.Show("変換エラー: " + ex.Message);
            }
        }

        private void btnSendSyx_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (converter == null || converter.GetOperatorCount() != 4)
                {
                    MessageBox.Show("音色データが不完全です。DMPファイルの内容を確認してください。");
                    return;
                }

                converter.ShiftOctave(GetSelectedOctaveShift());
                converter.SetVoiceName(txtVoiceName.Text.Trim());

                // 共通パラメータ設定
                converter.SetVoiceCommonParams(
                    GetComboValue(comboLfoSpeed),
                    GetComboValue(comboLfoDelay),
                    GetComboValue(comboPMD),
                    GetComboValue(comboAMD),
                    GetComboTagValue(comboLfoSync),
                    GetComboTagValue(comboLfoWave),
                    GetComboValue(comboPitchModSens),
                    GetComboValue(comboAmpModSens)
                );

                // パフォーマンスパラメータ設定
                converter.SetFunctionParams(
                    GetComboTagValue(comboTranspose, 24),
                    comboPolyMode.SelectedIndex == 1 ? 1 : 0,
                    GetComboValue(comboPitchBendRange),
                    GetComboTagValue(comboSustain),
                    GetComboTagValue(comboChorus),
                    GetComboTagValue(comboPortaMode),
                    GetComboValue(comboPortaTime),
                    GetComboValue(comboWheelPitch),
                    GetComboValue(comboWheelAmp),
                    GetComboValue(comboFootVolume),
                    GetComboTagValue(comboPortaFootSwitch)
                );

                // VCED SysEx データ生成
                var syx = converter.BuildSingleVoiceVCEDSysEx();
                if (syx == null)
                {
                    MessageBox.Show("SysExデータの生成に失敗しました。");
                    return;
                }

                // SysEx送信（MIDIデバイスに送る処理）
                if (comboMidiOut.SelectedItem is ComboBoxItem item && item.Tag is int deviceIndex)
                {
                    using var midiOut = new MidiOut(deviceIndex);
                    midiOut.SendBuffer(syx);
                    txtStatus.Text = "SysExを送信しました。";
                }
                else
                {
                    MessageBox.Show("MIDI出力デバイスを選択してください。");
                }

                txtStatus.Text = "SysEx送信完了";
                txtOutput.Text = converter.GetDx21VCEDSyxDataAsText();
                txtBinaryEditor.Text = HexFormatter.FormatHexDumpWithAsciiAndHeader(syx);
            }
            catch (Exception ex)
            {
                MessageBox.Show("送信エラー: " + ex.Message);
            }
        }


        private void btnSendSyxFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SysExファイル (*.syx)|*.syx|すべてのファイル (*.*)|*.*",
                Title = "SysExファイルを選択"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(dialog.FileName);

                    if (comboMidiOut.SelectedItem is ComboBoxItem item && item.Tag is int deviceIndex)
                    {
                        using var midiOut = new MidiOut(deviceIndex);
                        midiOut.SendBuffer(data);
                        txtStatus.Text = dialog.FileName + " : SysExを送信しました。";
                    }
                    else
                    {
                        MessageBox.Show("MIDI出力デバイスを選択してください。");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("送信エラー: " + ex.Message);
                }
            }
        }

        private void btnEnd_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private int GetSelectedOctaveShift()
        {
            return comboOctaveShift.SelectedItem is ComboBoxItem item &&
                   int.TryParse(item.Content.ToString()?.Replace("+", ""), out int shift)
                   ? shift : 0;
        }

        private int GetComboValue(ComboBox combo, int defaultValue = 0)
        {
            return int.TryParse(combo.Text, out int result)
                ? Math.Clamp(result, 0, 99)
                : defaultValue;
        }
        private int GetComboTagValue(ComboBox combo, int defaultValue = 0)
        {
            if (combo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int value))
            {
                return value;
            }
            return defaultValue;
        }


    }
}
