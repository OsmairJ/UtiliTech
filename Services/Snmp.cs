using System.Text;

namespace UtiliTech.Services;

/// <summary>
/// SNMP v1 mínimo, só leitura: monta um GET (comunidade "public") e lê os textos da resposta.
/// Usado para perguntar o modelo à impressora, como o assistente da Lexmark faz.
/// </summary>
public static class Snmp
{
    public static byte[] GetRequest(params string[] oids)
    {
        var varbinds = oids.SelectMany(o => Tlv(0x30, Tlv(0x06, EncodeOid(o)).Concat(new byte[] { 0x05, 0x00 }).ToArray())).ToArray();
        var pdu = Tlv(0xA0, Tlv(0x02, new byte[] { 0x01 })              // request-id
            .Concat(Tlv(0x02, new byte[] { 0x00 }))                      // error-status
            .Concat(Tlv(0x02, new byte[] { 0x00 }))                      // error-index
            .Concat(Tlv(0x30, varbinds)).ToArray());
        var message = Tlv(0x02, new byte[] { 0x00 })                    // versão 1
            .Concat(Tlv(0x04, Encoding.ASCII.GetBytes("public")))
            .Concat(pdu).ToArray();
        return Tlv(0x30, message);
    }

    public static byte[] EncodeOid(string oid)
    {
        var parts = oid.Split('.').Select(long.Parse).ToArray();
        var bytes = new List<byte> { (byte)(40 * parts[0] + parts[1]) };
        foreach (long part in parts.Skip(2))
        {
            long n = part;
            var chunk = new List<byte> { (byte)(n & 0x7F) };
            for (n >>= 7; n > 0; n >>= 7) chunk.Insert(0, (byte)((n & 0x7F) | 0x80));
            bytes.AddRange(chunk);
        }
        return bytes.ToArray();
    }

    static byte[] Tlv(byte tag, byte[] value)
    {
        byte[] len = value.Length switch
        {
            < 128 => new[] { (byte)value.Length },
            < 256 => new byte[] { 0x81, (byte)value.Length },
            _ => new byte[] { 0x82, (byte)(value.Length >> 8), (byte)value.Length },
        };
        return new[] { tag }.Concat(len).Concat(value).ToArray();
    }

    /// <summary>Valores da resposta, na ordem dos OIDs pedidos; o que não for texto vira "". Resposta estranha: lista vazia.</summary>
    public static List<string> ParseStrings(byte[] b)
    {
        var values = new List<string>();
        try
        {
            int p = 0;
            Read(b, ref p);                         // mensagem
            var v = Read(b, ref p); p += v.Len;     // versão
            var c = Read(b, ref p); p += c.Len;     // comunidade
            Read(b, ref p);                         // GetResponse
            for (int i = 0; i < 3; i++) { var t = Read(b, ref p); p += t.Len; } // id, erro, índice
            var list = Read(b, ref p);
            int end = list.Start + list.Len;
            while (p < end)
            {
                var vb = Read(b, ref p);
                int vbEnd = vb.Start + vb.Len;
                var oid = Read(b, ref p); p += oid.Len;
                var val = Read(b, ref p);
                values.Add(val.Tag == 0x04 ? Encoding.UTF8.GetString(b, val.Start, val.Len).Trim('\0', ' ') : "");
                p = vbEnd;
            }
        }
        catch (IndexOutOfRangeException) { values.Clear(); }
        catch (ArgumentException) { values.Clear(); }
        return values;
    }

    readonly record struct Node(byte Tag, int Start, int Len);

    static Node Read(byte[] b, ref int p)
    {
        byte tag = b[p++];
        int len = b[p++];
        if ((len & 0x80) != 0)
        {
            int n = len & 0x7F;
            len = 0;
            for (int i = 0; i < n; i++) len = (len << 8) | b[p++];
        }
        if (p + len > b.Length) throw new ArgumentException("resposta SNMP cortada");
        return new Node(tag, p, len);
    }
}
