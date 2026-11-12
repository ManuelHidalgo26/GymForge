using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GymForge.Application.Interfaces;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Contenedor cifrado <c>GFBK</c> v1 para lo que sale de la PC (copia externa y export). AES-256-GCM sobre
/// bloques troceados (memoria acotada aunque el payload sean cientos de MB de fotos); la clave se deriva de la
/// frase de acceso con PBKDF2-HMAC-SHA256 y un salt aleatorio por archivo. Formato completo en ADR-012.
///
/// Los errores de descifrado se informan como <see cref="DataPackageException"/> con un mensaje apto para la UI.
/// El descifrado escribe cada bloque ya autenticado, pero un truncado recién se detecta al final: el llamador
/// debe descartar el destino si <see cref="Decrypt(Stream, Stream, string)"/> lanza.
/// </summary>
public static class BackupCrypto
{
    public const int DefaultIterations = 600_000;
    public const int DefaultChunkSize = 1 << 20;

    private static readonly byte[] Magic = "GFBK"u8.ToArray();

    private const byte FormatVersion = 1;
    private const byte AlgorithmId = 1;   // PBKDF2-SHA256 / AES-256-GCM
    private const int SaltSize = 16;
    private const int NoncePrefixSize = 4;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int SaltOffset = 13;                              // tras magic, version, algId, flags, iterations y saltLen
    private const int NoncePrefixOffset = SaltOffset + SaltSize;
    private const int ChunkSizeOffset = NoncePrefixOffset + NoncePrefixSize;
    private const int HeaderSize = ChunkSizeOffset + 4;

    // Topes al leer: un header manipulado no puede forzar un PBKDF2 interminable ni un buffer gigante.
    private const uint MaxIterations = 10_000_000;
    private const uint MaxChunkSize = 64 * 1024 * 1024;

    public static bool LooksEncrypted(Stream stream)
    {
        var start = stream.Position;
        Span<byte> head = stackalloc byte[4];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = start;
        return read == head.Length && head.SequenceEqual(Magic);
    }

    public static void Encrypt(Stream plain, Stream cipher, string passphrase) =>
        Encrypt(plain, cipher, passphrase, DefaultIterations, DefaultChunkSize);

    internal static void Encrypt(Stream plain, Stream cipher, string passphrase, int iterations, int chunkSize)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        var header = BuildHeader(iterations, chunkSize);
        cipher.Write(header);

        var key = DeriveKey(passphrase, header.AsSpan(SaltOffset, SaltSize), iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            EncryptBlocks(aes, header, plain, cipher, chunkSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static void Decrypt(Stream cipher, Stream plain, string passphrase)
    {
        var header = ReadHeader(cipher, out var iterations, out var chunkSize);

        var key = DeriveKey(passphrase, header.AsSpan(SaltOffset, SaltSize), iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            DecryptBlocks(aes, header, cipher, plain, chunkSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // ── cifrado ─────────────────────────────────────────────────────────────

    private static byte[] BuildHeader(int iterations, int chunkSize)
    {
        var header = new byte[HeaderSize];
        var span = header.AsSpan();
        Magic.CopyTo(span);
        span[4] = FormatVersion;
        span[5] = AlgorithmId;
        // flags (2 bytes) reservados en 0
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)iterations);
        span[SaltOffset - 1] = SaltSize;
        RandomNumberGenerator.Fill(span.Slice(SaltOffset, SaltSize + NoncePrefixSize));
        BinaryPrimitives.WriteUInt32BigEndian(span[ChunkSizeOffset..], (uint)chunkSize);
        return header;
    }

    /// <summary>
    /// Lee de a un bloque con un bloque de anticipación: así se sabe cuál es el último (<c>isFinal</c>)
    /// sin cargar el payload entero.
    /// </summary>
    private static void EncryptBlocks(AesGcm aes, byte[] header, Stream plain, Stream cipher, int chunkSize)
    {
        var current = new byte[chunkSize];
        var next = new byte[chunkSize];
        var encrypted = new byte[chunkSize];
        var tag = new byte[TagSize];

        var currentLength = Fill(plain, current);
        for (ulong index = 0; ; index++)
        {
            var nextLength = currentLength == chunkSize ? Fill(plain, next) : 0;
            var isFinal = nextLength == 0;

            aes.Encrypt(
                Nonce(header, index), current.AsSpan(0, currentLength), encrypted.AsSpan(0, currentLength),
                tag, Aad(header, index, isFinal));
            WriteBlock(cipher, isFinal, encrypted.AsSpan(0, currentLength), tag);

            if (isFinal) return;
            (current, next) = (next, current);
            currentLength = nextLength;
        }
    }

    private static void WriteBlock(Stream cipher, bool isFinal, ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> tag)
    {
        Span<byte> prefix = stackalloc byte[5];
        prefix[0] = isFinal ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(prefix[1..], (uint)encrypted.Length);
        cipher.Write(prefix);
        cipher.Write(encrypted);
        cipher.Write(tag);
    }

    /// <summary>Llena el buffer lo más posible; devuelve cuántos bytes quedaron (menos que el tamaño solo al final del stream).</summary>
    private static int Fill(Stream source, byte[] buffer) => source.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

    // ── descifrado ──────────────────────────────────────────────────────────

    private static byte[] ReadHeader(Stream cipher, out int iterations, out int chunkSize)
    {
        var header = new byte[HeaderSize];
        if (cipher.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length
            || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new DataPackageException("Este archivo no es un respaldo cifrado de GymForge.");

        if (header[4] != FormatVersion || header[5] != AlgorithmId)
            throw new DataPackageException(
                "El respaldo fue creado con una versión más nueva de GymForge. Actualizá la app antes de abrirlo.");

        var iterationCount = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        var chunk = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(ChunkSizeOffset));
        if (header[SaltOffset - 1] != SaltSize || iterationCount is 0 or > MaxIterations || chunk is 0 or > MaxChunkSize)
            throw new DataPackageException("El respaldo cifrado está dañado.");

        iterations = (int)iterationCount;
        chunkSize = (int)chunk;
        return header;
    }

    private static void DecryptBlocks(AesGcm aes, byte[] header, Stream cipher, Stream plain, int chunkSize)
    {
        var encrypted = new byte[chunkSize];
        var decrypted = new byte[chunkSize];
        var tag = new byte[TagSize];
        var prefix = new byte[5];

        for (ulong index = 0; ; index++)
        {
            ReadExactly(cipher, prefix);
            var isFinal = prefix[0] switch
            {
                0 => false,
                1 => true,
                _ => throw new DataPackageException("El respaldo cifrado está dañado."),
            };
            var length = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(1));
            if (length > chunkSize) throw new DataPackageException("El respaldo cifrado está dañado.");

            ReadExactly(cipher, encrypted.AsSpan(0, (int)length));
            ReadExactly(cipher, tag);
            DecryptBlock(aes, header, index, isFinal, encrypted.AsSpan(0, (int)length), tag, decrypted);
            plain.Write(decrypted, 0, (int)length);

            if (!isFinal) continue;
            if (cipher.ReadByte() != -1)
                throw new DataPackageException("El respaldo cifrado está dañado (datos de más al final).");
            return;
        }
    }

    private static void DecryptBlock(
        AesGcm aes, byte[] header, ulong index, bool isFinal,
        ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> tag, byte[] decrypted)
    {
        try
        {
            aes.Decrypt(Nonce(header, index), encrypted, tag, decrypted.AsSpan(0, encrypted.Length), Aad(header, index, isFinal));
        }
        catch (CryptographicException ex)
        {
            throw new DataPackageException("La frase de acceso es incorrecta, o el archivo está dañado.", ex);
        }
    }

    private static void ReadExactly(Stream cipher, Span<byte> buffer)
    {
        if (cipher.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length)
            throw new DataPackageException("El respaldo cifrado está truncado o dañado.");
    }

    // ── primitivas ──────────────────────────────────────────────────────────

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations)
    {
        var secret = Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormC));
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>nonce_i = noncePrefix(4) || UInt64BE(i): único por bloque dentro de un archivo (y la clave lo es por archivo).</summary>
    private static byte[] Nonce(byte[] header, ulong index)
    {
        var nonce = new byte[NonceSize];
        header.AsSpan(NoncePrefixOffset, NoncePrefixSize).CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(NoncePrefixSize), index);
        return nonce;
    }

    /// <summary>aad_i = header || UInt64BE(i) || isFinal: ata el bloque al archivo, a su posición y a la marca de fin.</summary>
    private static byte[] Aad(byte[] header, ulong index, bool isFinal)
    {
        var aad = new byte[HeaderSize + 8 + 1];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(HeaderSize), index);
        aad[^1] = isFinal ? (byte)1 : (byte)0;
        return aad;
    }
}
