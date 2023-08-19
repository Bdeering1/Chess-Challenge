using System;
using System.Collections.Generic;


public class PstPacker
{
    public enum ScoreType
    {
        PawnMG, KnightMG, BishopMG, RookMG, QueenMG, KingMG,
        PawnEG, KnightEG, BishopEG, RookEG, QueenEG, KingEG
    }

    private static int[] mg_pawn_table = {
      0,   0,   0,   0,
     98, 127,  61,  95,
     -6,   7,  26,  31,
    -14,  13,   6,  21,
    -27,  -2,  -5,  12,
    -26,  -4,  -4, -10,
    -35,  -1, -20, -23,
      0,   0,   0,   0,
    };

    private static int[] eg_pawn_table = {
      0,   0,   0,   0,
    127, 127, 127, 127,
     94, 100,  85,  67,
     32,  24,  13,   5,
     13,   9,  -3,  -7,
      4,   7,  -6,   1,
     13,   8,   8,  10,
      0,   0,   0,   0,
    };

    private static int[] mg_knight_table = {
    -128, -89, -34, -49,
     -73, -41,  72,  36,
     -47,  60,  37,  65,
      -9,  17,  19,  53,
     -13,   4,  16,  13,
     -23,  -9,  12,  10,
     -29, -53, -12,  -3,
    -105, -21, -58, -33,
    };

    private static int[] eg_knight_table = {
    -58, -38, -13, -28,
    -25,  -8, -25,  -2,
    -24, -20,  10,   9,
    -17,   3,  22,  22,
    -18,  -6,  16,  25,
    -23,  -3,  -1,  15,
    -42, -20, -10,  -5,
    -29, -51, -23, -15,
    };

    private static int[] mg_bishop_table = {
    -29,   4, -82, -37,
    -26,  16, -18, -13,
    -16,  37,  43,  40,
     -4,   5,  19,  50,
     -6,  13,  13,  26,
      0,  15,  15,  15,
      4,  15,  16,   0,
    -33,  -3, -14, -21,
    };

    private static int[] eg_bishop_table = {
    -14, -21, -11,  -8,
     -8,  -4,   7, -12,
      2,  -8,   0,  -1,
     -3,   9,  12,   9,
     -6,   3,  13,  19,
    -12,  -3,   8,  10,
    -14, -18,  -7,  -1,
    -23,  -9, -23,  -5,
    };

    private static int[] mg_rook_table = {
     32,  42,  32,  51,
     27,  32,  58,  62,
     -5,  19,  26,  36,
    -24, -11,   7,  26,
    -36, -26, -12,  -1,
    -45, -25, -16, -17,
    -44, -16, -20,  -9,
    -19, -13,   1,  17,
    };

    private static int[] eg_rook_table = {
    13, 10, 18, 15, 12,
    11, 13, 13, 11, -3,
     7,  7,  7,  5,  4,
     4,  3, 13,  1,  2,
     3,  5,  8,  4, -5,
    -4,  0, -5, -1, -7,
    -6, -6,  0,  2, -9,
    -9,  2,  3, -1, -5,
    };

    private static int[] mg_queen_table = {
    -28,   0,  29,  12,
    -24, -39,  -5,   1,
    -13, -17,   7,   8,
    -27, -27, -16, -16,
     -9, -26,  -9, -10,
    -14,   2, -11,  -2,
    -35,  -8,  11,   2,
     -1, -18,  -9,  10,
    };

    private static int[] eg_queen_table = {
     -9,  22,  22,  27,
    -17,  20,  32,  41,
    -20,   6,   9,  49,
      3,  22,  24,  45,
    -18,  28,  19,  47,
    -16, -27,  15,   6,
    -22, -23, -30, -16,
    -33, -28, -22, -43,
    };

    private static int[] mg_king_table = {
    -65,  23,  16, -15,
     29,  -1, -20,  -7,
     -9,  24,   2, -16,
    -17, -20, -12, -27,
    -49,  -1, -27, -39,
    -14, -14, -22, -46,
      1,   7,  -8, -64,
    -15,  36,  12, -54,
    };

    private static int[] eg_king_table = {
    -74, -35, -18, -18,
    -12,  17,  14,  17,
     10,  17,  23,  15,
     -8,  22,  24,  27,
    -18,  -4,  21,  24,
    -19,  -3,  11,  21,
    -27, -11,   4,  13,
    -53, -34, -21, -11, 
    };


    public static ulong[] Generate()
    {
        List<int[]> table = new()
        {
            mg_pawn_table,
            mg_knight_table,
            mg_bishop_table,
            mg_rook_table,
            mg_queen_table,
            mg_king_table,

            eg_pawn_table,
            eg_knight_table,
            eg_bishop_table,
            eg_rook_table,
            eg_queen_table,
            eg_king_table
        };

        Console.WriteLine("Packed table:\n");
        ulong[] packedData = PackData(table);

        //Console.WriteLine("Unpacked table:\n");
        //UnpackData(packedData);

        return packedData;
    }

    private const int tableWidth = 4;
    private const int tableHeight = 8;

    // Packs data in the following form
    // ulong[(set * tableWidth) + file] = rank
    private static ulong[] PackData(List<int[]> tablesToPack)
    {
        ulong[] packedData = new ulong[tablesToPack.Count * tableWidth];

        for (int set = 0; set < tablesToPack.Count; set++)
        {
            int[] setToPack = tablesToPack[set];

            for (int file = 0; file < tableWidth; file++)
            {
                ulong packedfile = 0;
                for (int rank = 0; rank < tableHeight; rank++)
                {
                    sbyte valueToPack = (sbyte)setToPack[(rank * tableWidth) + file];
                    packedfile |= (ulong)(valueToPack & 0xFF) << rank * 8;
                }
                packedData[(set * tableWidth) + file] = packedfile;
            }
        }

        Console.WriteLine("{ ");
        for (int set = 0; set < tablesToPack.Count; set++)
        {
            for (int file = 0; file < tableWidth; file++)
            {
                Console.Write($"0x{packedData[(set * tableWidth) + file]:X}, ");
            }
            Console.WriteLine();
        }
        Console.WriteLine("};");

        return packedData;
    }

    private static void UnpackData(ulong[] tablesToUnpack)
    {
        Console.WriteLine("Count: " + tablesToUnpack.Length);

        // Print tables to middlegame scores
        for (int type = 0; type < tablesToUnpack.Length/tableWidth; type++)
        {
            Console.WriteLine("\n\nTable for type: " + (ScoreType)type);
            for (int rank = 0; rank < tableHeight; rank++)
            {
                for (int file = 0; file < tableWidth*2; file++)
                {
                    Console.Write($"{GetSquareBonus(tablesToUnpack, type, true, rank, file > 3 ? 7-file : file),4}" + ", ");
                }
                Console.WriteLine();
            }
        }
    }

    private static int GetSquareBonus(ulong[] tables, int type, bool isWhite, int rank, int file)
    {
        // Mirror vertically for white pieces, since piece arrays are flipped vertically
        if (!isWhite)
            file = 3 - file;

        // Grab the correct byte representing the value
        sbyte squareValue = unchecked((sbyte)((tables[(type * tableWidth) + file] >> rank * 8) & 0xFF));

        // And multiply it by the reduction factor to get our original value again
        int value = (int)squareValue;

        // Invert eval scores for black pieces
        return isWhite ? value : -value;
    }
}