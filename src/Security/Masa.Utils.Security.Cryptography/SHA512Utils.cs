namespace Masa.Utils.Security.Cryptography;

/// <summary>
/// Hash algorithm encryption SHA512
/// </summary>
public class SHA512Utils : HashAlgorithmBase
{
    /// <summary>
    /// Encrypt string with SHA512
    /// </summary>
    /// <param name="content">String to be encrypted</param>
    /// <param name="isToLower">Whether to convert the encrypted string to lowercase</param>
    /// <param name="encoding">Encoding format, default UTF-8</param>
    /// <returns>encrypted result</returns>
    public static string Encrypt(string content, bool isToLower = false, Encoding? encoding = null)
        => Encrypt(EncryptType.Sha512, content, isToLower, encoding);
}
