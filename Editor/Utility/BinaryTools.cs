using System;

namespace JaxTools.StateSync.Utility
{
    public static class BinaryTools
    {
        public const int IntSuggestionThreshold = 8;

        public static int GetRequiredBooleanCount(int stateCount)
        {
            if (stateCount <= 1) return 0;

            int bits = 0;
            int maxStates = 1;
            while (maxStates < stateCount && bits < 31)
            {
                bits++;
                maxStates <<= 1;
            }
            return bits;
        }

        public static bool ShouldSuggestInt(int booleanCount)
        {
            return booleanCount >= IntSuggestionThreshold;
        }

        // Index 0 is least-significant bit.
        public static bool[] ToBooleanArray(int value, int booleanCount)
        {
            if (booleanCount <= 0) return Array.Empty<bool>();
            if (value < 0) value = 0;

            var result = new bool[booleanCount];
            for (int i = 0; i < booleanCount; i++)
            {
                result[i] = (value & (1 << i)) != 0;
            }
            return result;
        }

    }
}
