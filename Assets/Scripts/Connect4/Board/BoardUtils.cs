/// <summary>
/// Class for board utilitaries
/// </summary>
public class BoardUtils
{
    // Updated board dimensions for Connect 5
    public static readonly int NUM_ROWS = 8;
    public static readonly int NUM_COLS = 9;

    // Updated evaluation matrix for 8x9 Connect 5 board
    // Center positions are more valuable for 5-in-a-row
    private static int[,] evaluationBoard = new int[,]{
        {1, 2, 3, 4, 5, 4, 3, 2, 1},
        {2, 3, 5, 6, 7, 6, 5, 3, 2},
        {3, 5, 8, 10, 12, 10, 8, 5, 3},
        {4, 6, 10, 14, 16, 14, 10, 6, 4},
        {4, 6, 10, 14, 16, 14, 10, 6, 4},
        {3, 5, 8, 10, 12, 10, 8, 5, 3},
        {2, 3, 5, 6, 7, 6, 5, 3, 2},
        {1, 2, 3, 4, 5, 4, 3, 2, 1}
    };

    /// <summary>
    /// Function for getting the default empty board
    /// </summary>
    /// <returns>the default empty board for Connect 5</returns>
    public static Tile[,] GetDefaultBoard()
    {
        Tile[,] board = new Tile[NUM_ROWS, NUM_COLS];
        for (int i = 0; i < NUM_ROWS; i++)
        {
            for (int j = 0; j < NUM_COLS; j++)
            {
                board[i, j] = Tile.EMPTY;
            }
        }

        return board;
    }

    /// <summary>
    /// Function to evaluate a board from the given player perspective
    /// </summary>
    /// <param name="board"></param>
    /// <param name="alliance"></param>
    /// <returns></returns>
    public static double EvaluateBoard(Board board, PlayerAlliance alliance)
    {
        if (Connect4Utils.Finished(board))
        {
            return Connect4Utils.INF;
        }

        double score = 0;
        for (int i = 0; i < NUM_ROWS; i++)
        {
            for (int j = 0; j < NUM_COLS; j++)
            {
                if (board.Table[i, j] != Tile.EMPTY)
                {
                    if (board.Table[i, j] == Tile.RED && alliance == PlayerAlliance.RED)
                    {
                        score += evaluationBoard[i, j];
                    }
                    else if (board.Table[i, j] == Tile.BLACK && alliance == PlayerAlliance.BLACK)
                    {
                        score += evaluationBoard[i, j];
                    }
                }
            }
        }

        return score;
    }
}