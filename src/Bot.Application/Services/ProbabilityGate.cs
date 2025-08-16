using System.Security.Cryptography;

namespace Bot.Application.Services;

public static class ProbabilityGate
{
    public static bool Hit(double probability)
    {
        probability = Math.Clamp(probability, 0, 1);

        var value = RandomNumberGenerator.GetInt32(1, 10_000) / 10_000.0;

        return value < probability;
    }
}