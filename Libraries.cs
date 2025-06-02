using System;
using System.Linq;
using System.Text;

namespace Library
{
    public static class HexFormatter
    {
        /// <summary>
        /// バイト配列を1行16バイトの16進表記テキストに変換します。
        /// </summary>
        public static string FormatHexDump(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                int lineLength = Math.Min(16, data.Length - i);
                var lineBytes = data.Skip(i).Take(lineLength);

                // 16進数部分
                var hexPart = string.Join(" ", lineBytes.Select(b => b.ToString("X2")));
                sb.AppendLine(hexPart);
            }
            return sb.ToString();
        }
        public static string FormatHexDumpWithAsciiAndHeader(byte[] data)
        {
            var sb = new StringBuilder();

            // ヘッダ行（バイトインデックス）
            sb.Append("        ");
            for (int i = 0; i < 16; i++)
            {
                sb.Append(i.ToString("X2"));
                sb.Append(i == 15 ? "\n" : (i == 7 ? "  " : " "));
            }

            // データ行
            for (int i = 0; i < data.Length; i += 16)
            {
                var bytes = data.Skip(i).Take(16).ToArray();

                // アドレス
                sb.Append(i.ToString("X6"));
                sb.Append("  ");

                // 16進数（8+8）
                for (int j = 0; j < 16; j++)
                {
                    if (j < bytes.Length)
                        sb.Append(bytes[j].ToString("X2") + " ");
                    else
                        sb.Append("   ");
                    if (j == 7) sb.Append(" "); // 中央スペース
                }

                sb.Append(" | ");

                // ASCII表記
                foreach (byte b in bytes)
                {
                    sb.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

    }
}
