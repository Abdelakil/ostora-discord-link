using System.Security.Cryptography;
using System.Text;

namespace OstoraDiscordLink.Services;

public class CodeGenerationService
{
    private static readonly string[] _allowedChars = 
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", 
        "N", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "2", "3", "4", "5", "6", "7", "8", "9"
    ];

    public string GenerateCode(int length)
    {
        if (length <= 0)
            throw new ArgumentException("Code length must be greater than 0", nameof(length));

        if (length > 32)
            throw new ArgumentException("Code length cannot exceed 32 characters", nameof(length));

        var result = new StringBuilder(length);
        using var rng = RandomNumberGenerator.Create();
        
        for (int i = 0; i < length; i++)
        {
            var randomIndex = GetRandomInt(rng, 0, _allowedChars.Length);
            result.Append(_allowedChars[randomIndex]);
        }

        return result.ToString();
    }

    private static int GetRandomInt(RandomNumberGenerator rng, int min, int max)
    {
        if (min >= max)
            throw new ArgumentException("min must be less than max");

        var range = (uint)(max - min);
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        var randomValue = BitConverter.ToUInt32(bytes, 0);
        
        return (int)(randomValue % range) + min;
    }
}
