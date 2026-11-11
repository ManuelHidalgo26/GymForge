using System.Security.Cryptography;
using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Infrastructure.Persistence;

namespace GymForge.Integration.Tests;

/// <summary>Contenedor cifrado GFBK v1 (ADR-012): round-trip, troceo, frase incorrecta, manipulación y memoria.</summary>
public class BackupCryptoTests
{
    private const string Pass = "una frase de prueba bien larga";
    private const int FastIterations = 1_000;   // los tests no necesitan los 600.000 de producción

    private static byte[] RandomBytes(int length)
    {
        var data = new byte[length];
        RandomNumberGenerator.Fill(data);
        return data;
    }

    private static byte[] Encrypt(byte[] payload, int chunkSize = BackupCrypto.DefaultChunkSize, string pass = Pass)
    {
        using var plain = new MemoryStream(payload);
        using var cipher = new MemoryStream();
        BackupCrypto.Encrypt(plain, cipher, pass, FastIterations, chunkSize);
        return cipher.ToArray();
    }

    private static byte[] Decrypt(byte[] container, string pass = Pass)
    {
        using var cipher = new MemoryStream(container);
        using var plain = new MemoryStream();
        BackupCrypto.Decrypt(cipher, plain, pass);
        return plain.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]       // exactamente un bloque
    [InlineData(1025)]       // un bloque lleno + uno de 1 byte
    [InlineData(10 * 1024)]  // múltiplo exacto: el último bloque lleno es el final
    [InlineData(10 * 1024 + 7)]
    public void RoundTrip_MultiBloque_DevuelveElMismoPayload(int length)
    {
        var payload = RandomBytes(length);

        var result = Decrypt(Encrypt(payload, chunkSize: 1024));

        result.Should().Equal(payload);
    }

    [Fact]
    public void RoundTrip_ConElTamanoDeBloqueDeProduccion_SuperandoUnBloque()
    {
        var payload = RandomBytes(BackupCrypto.DefaultChunkSize * 2 + 123);

        Decrypt(Encrypt(payload)).Should().Equal(payload);
    }

    [Fact]
    public void RoundTrip_ConIteracionesDeProduccion()
    {
        var payload = RandomBytes(5000);
        using var plain = new MemoryStream(payload);
        using var cipher = new MemoryStream();

        BackupCrypto.Encrypt(plain, cipher, Pass);

        Decrypt(cipher.ToArray()).Should().Equal(payload);
    }

    [Fact]
    public void Encrypt_DaSalidasDistintasParaElMismoPayload_PorSaltYNonceAleatorios()
    {
        var payload = RandomBytes(100);

        Encrypt(payload).Should().NotEqual(Encrypt(payload));
    }

    [Fact]
    public void Encrypt_EmpiezaConElMagicYNoDejaElTextoPlanoALaVista()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(new string('A', 500) + "dato-sensible");

        var container = Encrypt(payload);

        container.Take(4).Should().Equal("GFBK"u8.ToArray());
        System.Text.Encoding.Latin1.GetString(container).Should().NotContain("dato-sensible");
    }

    [Fact]
    public void Decrypt_ConFraseIncorrecta_LanzaMensajeClaro()
    {
        var container = Encrypt(RandomBytes(100));

        var act = () => Decrypt(container, "otra frase que no es la correcta");

        act.Should().Throw<DataPackageException>()
            .WithMessage("La frase de acceso es incorrecta, o el archivo está dañado.");
    }

    [Fact]
    public void Decrypt_NormalizaLaFrase_FormasUnicodeEquivalentesSirvenIgual()
    {
        var compuesta = "contraseña-ñandú-larga";           // ñ, ú precompuestas (FormC)
        var descompuesta = compuesta.Normalize(System.Text.NormalizationForm.FormD);
        var payload = RandomBytes(50);

        Decrypt(Encrypt(payload, pass: compuesta), descompuesta).Should().Equal(payload);
    }

    [Fact]
    public void Decrypt_ConUnByteAlteradoEnCualquierZona_NuncaDevuelveDatosEnSilencio()
    {
        var payload = RandomBytes(3000);
        var container = Encrypt(payload, chunkSize: 1024);

        // Se altera cada byte, uno por vez (header, longitudes, ciphertext, tags): ninguno puede pasar.
        for (var i = 0; i < container.Length; i++)
        {
            var tampered = (byte[])container.Clone();
            tampered[i] ^= 0x01;

            var act = () => Decrypt(tampered);

            act.Should().Throw<DataPackageException>($"alterar el byte {i} tiene que detectarse");
        }
    }

    [Fact]
    public void Decrypt_ConElArchivoTruncado_LoDetecta()
    {
        var container = Encrypt(RandomBytes(3000), chunkSize: 1024);

        // Cortar justo en el borde de un bloque (sin tocar los demás) tampoco puede pasar como válido.
        foreach (var cut in new[] { 1, 16, container.Length / 2, container.Length - 1 })
        {
            var act = () => Decrypt(container[..^cut]);

            act.Should().Throw<DataPackageException>($"faltan {cut} bytes");
        }
    }

    [Fact]
    public void Decrypt_SiSeQuitaElUltimoBloqueEntero_FallaPorFaltarElMarcadorFinal()
    {
        var chunk = 1024;
        var container = Encrypt(RandomBytes(chunk * 2), chunkSize: chunk);   // 2 bloques llenos + bloque final vacío
        var finalBlockSize = 1 + 4 + 0 + 16;

        var act = () => Decrypt(container[..^finalBlockSize]);

        act.Should().Throw<DataPackageException>().WithMessage("*truncado*");
    }

    [Fact]
    public void Decrypt_ConBytesDeMasAlFinal_Falla()
    {
        var container = Encrypt(RandomBytes(100)).Concat(new byte[] { 0 }).ToArray();

        var act = () => Decrypt(container);

        act.Should().Throw<DataPackageException>();
    }

    [Fact]
    public void Decrypt_DeUnArchivoQueNoEsGfbk_DiceQueNoEsUnRespaldoCifrado()
    {
        var act = () => Decrypt(RandomBytes(200));

        act.Should().Throw<DataPackageException>().WithMessage("*no es un respaldo cifrado*");
    }

    [Fact]
    public void Decrypt_DeUnaVersionMasNueva_PideActualizar()
    {
        var container = Encrypt(RandomBytes(10));
        container[4] = 2;   // version

        var act = () => Decrypt(container);

        act.Should().Throw<DataPackageException>().WithMessage("*versión más nueva*");
    }

    [Fact]
    public void LooksEncrypted_DistingueGfbkDeUnZipYDeUnArchivoVacio()
    {
        using var gfbk = new MemoryStream(Encrypt(RandomBytes(10)));
        using var zip = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2, 3]);
        using var empty = new MemoryStream();

        BackupCrypto.LooksEncrypted(gfbk).Should().BeTrue();
        BackupCrypto.LooksEncrypted(zip).Should().BeFalse();
        BackupCrypto.LooksEncrypted(empty).Should().BeFalse();
        gfbk.Position.Should().Be(0, "no consume el stream");
    }

    [Fact]
    public void Encrypt_DeUnPayloadDe300MB_NoCargaTodoEnMemoria()
    {
        const long size = 300L * 1024 * 1024;
        using var source = new SyntheticStream(size);
        using var sink = new CountingStream();
        var before = GC.GetAllocatedBytesForCurrentThread();

        BackupCrypto.Encrypt(source, sink, Pass, FastIterations, BackupCrypto.DefaultChunkSize);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        sink.Length.Should().BeGreaterThan(size);
        allocated.Should().BeLessThan(64L * 1024 * 1024, "el troceo mantiene la memoria acotada a unos pocos bloques de 1 MiB");
    }

    /// <summary>Stream de solo lectura que fabrica bytes sin guardarlos: simula un export enorme sin tocar disco.</summary>
    private sealed class SyntheticStream(long length) : Stream
    {
        private long _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            Array.Fill(buffer, (byte)0x5A, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Descarta lo escrito y solo cuenta cuántos bytes fueron.</summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        public override void Write(byte[] buffer, int offset, int count) => _length += count;
        public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
