namespace Reocla;

static class EntryPoint
{
    public static void Main(string[] args)
    {
        var ctx = new Evaluator();

        if (args.Length > 0)
        {
            string filename = Path.GetFullPath(args[0]);
            try
            {
                string content = File.ReadAllText(filename);
                ctx.Filename = filename;
                ctx.Eval(Parser.Parse(content, filename));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n{ex.Message}");
                Console.Error.WriteLine($"\nStack trace:\n{ex.StackTrace}");
            }
        }
        else
        {
            // REPL
            while (true)
            {
                Console.Write("> ");
                string? line = Console.ReadLine();
                if (line == null || line.Trim() == "quit") break;
                try
                {
                    var obj = Parser.Parse(line);
                    ctx.Eval(obj);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }
}
