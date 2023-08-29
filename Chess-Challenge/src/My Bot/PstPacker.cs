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
     25,  30,  32,  37,
      0,  10,  12,  17,
     -5,   5,   7,  12,
    -10,   0,   5,   5,
    -10,   3,   2,   2,
    -15,  -5,  -5, -15,
      0,   0,   0,   0,
    };

    private static int[] eg_pawn_table = {
     0,   0,   0,   0,   0, 
    60,  60,  60,  60,  60,
    30,  30,  30,  30,  30,
    10,  10,  10,  10,  10,
     0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,
     2,   2,   2,   2,   2,
     0,   0,   0,   0,   0,
    };

    private static int[] mg_knight_table = {
    -20, -13, -10, -10,
     -8,  -3,   2,   2,
      0,   7,  15,  15,
     -5,   2,  10,  10,
     -8,  -1,   7,   7,
    -10,  -3,   4,   5,
    -13,  -8,  -3,   1,
    -20, -13, -10, -10,
    };

    private static int[] eg_knight_table = {
    -10,  -7,  -5,  -5,
     -7,  -2,  -2,  -2,
     -5,  -2,   5,   5,
     -5,  -2,   5,   5,
     -5,  -2,   5,   5,
     -5,  -2,   5,   5,
     -7,  -2,  -2,  -2,
    -10,  -7,  -5,  -5,
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
   -8,  3,  -1,  -5,
    5,  9,   5,   4,
    2,  5,   9,   9,
   -2,  4,   9,  14,
   -2,  4,   9,  14,
    2,  5,   9,   9,
    3,  9,   5,   4,
   -8,  3,   2,  -2,
    };

    private static int[] mg_rook_table = {
      5,   5,  10,  12,
     15,  15,  20,  22,
      0,   0,   5,   7,
      0,   0,   5,   7,
      0,   0,   5,   7,
      0,   0,   5,   7,
      0,   0,   5,   7,
      0,   0,   5,   7,
    };

    private static int[] eg_rook_table = {
     0,   5,   5,   5,
     5,   5,   5,   5,
     0,   0,   0,   0,
     0,   0,   0,   0,
     0,   0,   0,   0,
     0,   0,   0,   0,
     0,   0,   0,   0,
    -5,   0,   0,   0,
    };

    private static int[] mg_queen_table = {
     -2,   0,   0,   0,
      0,   2,   2,   2,
      0,   2,   2,   2,
     -9,  -7,  -7,  -7,
     -9,  -7,  -7,  -7,
     -9,  -7,  -7,  -7,
      0,   2,   2,   2,
     -4,  -2,  -2,  -2,
    };

    private static int[] eg_queen_table = {
     1,   3,   3,   3,
     3,   5,   5,   5,
     3,   5,   5,   5,
     3,   5,   5,   5,
    -2,   0,   0,   0,
    -2,   0,   0,   0,
    -2,   0,   0,   0,
    -4,  -2,  -2,  -2,
    };

    private static int[] mg_king_table = {
      0,   0,  -5, -10,
      0,   0,  -5, -10,
     -5,  -5, -10, -15,
    -10, -10, -10, -20,
    -10, -10, -10, -20,
     -5,  -5, -10, -15,
      0,   0,  -5, -10,
      0,   0,   5, -10,
    };

    private static int[] eg_king_table = {
    -20, -10,   0,   5,
    -10,   0,  10,  15,
      0,  10,  20,  25,
      5,  15,  25,  30,
      5,  15,  25,  30,
      0,  10,  20,  25,
    -10,   0,  10,  15,
    -20, -10,   0,   5,
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

        ApplyNoise(table);

        ulong[] packedData = PackData(table);

        // Console.WriteLine("Unpacked table:\n");
        // UnpackData(packedData);

        return packedData;
    }

    private const int tableWidth = 4;
    private const int tableHeight = 8;

    private static void ApplyNoise(List<int[]> tables) {
        var rand = new Random();
        foreach (var table in tables) {
            for (int file = 0; file < tableWidth; file++)
            {
                for (int rank = 0; rank < tableHeight; rank++)
                {
                    table[(rank * tableWidth) + file] = table[(rank * tableWidth) + file] + rand.Next(-1, 2);
                }
            }
        }
    }

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

        return packedData;
    }

    private static void UnpackData(ulong[] tablesToUnpack)
    {
        Console.WriteLine("Count: " + tablesToUnpack.Length);

        // Print tables to middlegame scores
        for (int type = 0; type < tablesToUnpack.Length / tableWidth; type++)
        {
            Console.WriteLine("\n\nTable for type: " + (ScoreType)type);
            for (int rank = 0; rank < tableHeight; rank++)
            {
                for (int file = 0; file < tableWidth /* * 2 */; file++)
                {
                    Console.Write($"{GetSquareBonus(tablesToUnpack, type, true, rank, file > 3 ? 7 - file : file), 3}, ");
                }
                Console.WriteLine();
            }
        }
    }

    private static int GetSquareBonus(ulong[] tables, int type, bool isWhite, int rank, int file)
    {
        // Mirror vertically for white pieces, since piece arrays are flipped vertically
        //if (!isWhite) file = 3 - file;

        // Grab the correct byte representing the value
        sbyte squareValue = unchecked((sbyte)((tables[(type * tableWidth) + file] >> rank * 8) & 0xFF));

        // And multiply it by the reduction factor to get our original value again
        int value = (int)squareValue;

        // Invert eval scores for black pieces
        return isWhite ? value : -value;
    }
}