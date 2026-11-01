// Arden's Theorem Solver - DFA to Regular Expression (native Windows desktop app)
// Compiles with the C# compiler that ships inside Windows (.NET Framework 4.x).
// Source is plain ASCII on purpose (unicode written as \u escapes).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ArdensSolver
{
    // ------------------------------------------------------------------
    //  Regular-expression AST with algebraic simplification
    // ------------------------------------------------------------------
    class Node
    {
        public string T;            // empty, eps, sym, union, cat, star
        public string C;            // symbol text
        public List<Node> Items;    // union / cat
        public Node R;              // star operand
        public string K;            // canonical key
    }

    static class Rx
    {
        public static readonly Node Empty = Leaf("empty", "\u2205");
        public static readonly Node Eps = Leaf("eps", "\u03b5");

        static Node Leaf(string t, string k)
        {
            Node n = new Node();
            n.T = t;
            n.K = k;
            return n;
        }

        public static bool Same(Node a, Node b) { return a.K == b.K; }

        public static Node Sym(string c)
        {
            Node n = Leaf("sym", c);
            n.C = c;
            return n;
        }

        static Node Mk(Node n)
        {
            n.K = ToStr(n, 0);
            return n;
        }

        public static string ToStr(Node r) { return ToStr(r, 0); }

        // ctx: 0 top level, 1 inside concatenation, 2 inside star
        public static string ToStr(Node r, int ctx)
        {
            switch (r.T)
            {
                case "empty": return "\u2205";
                case "eps": return "\u03b5";
                case "sym": return r.C;
                case "union":
                    {
                        string s = string.Join("+", r.Items.Select(q => ToStr(q, 0)).ToArray());
                        return ctx >= 1 ? "(" + s + ")" : s;
                    }
                case "cat":
                    {
                        string s = string.Join("", r.Items.Select(q => ToStr(q, 1)).ToArray());
                        return ctx >= 2 ? "(" + s + ")" : s;
                    }
                default:
                    return ToStr(r.R, 2) + "*";
            }
        }

        public static bool Nullable(Node r)
        {
            switch (r.T)
            {
                case "eps":
                case "star": return true;
                case "empty":
                case "sym": return false;
                case "union": return r.Items.Any(q => Nullable(q));
                default: return r.Items.All(q => Nullable(q));
            }
        }

        public static Node Star(Node r)
        {
            if (Same(r, Empty) || Same(r, Eps)) return Eps;
            if (r.T == "star") return r;
            if (r.T == "union" && r.Items.Any(q => Same(q, Eps)))
                return Star(Union(r.Items.Where(q => !Same(q, Eps)).ToArray()));
            Node n = new Node();
            n.T = "star";
            n.R = r;
            return Mk(n);
        }

        public static Node Cat(params Node[] xs)
        {
            List<Node> flat = new List<Node>();
            foreach (Node x in xs)
            {
                if (Same(x, Empty)) return Empty;
                if (Same(x, Eps)) continue;
                if (x.T == "cat") flat.AddRange(x.Items); else flat.Add(x);
            }
            if (flat.Count == 0) return Eps;
            if (flat.Count == 1) return flat[0];
            Node n = new Node();
            n.T = "cat";
            n.Items = flat;
            return Mk(n);
        }

        static List<Node> Dedupe(List<Node> a)
        {
            HashSet<string> seen = new HashSet<string>();
            List<Node> o = new List<Node>();
            foreach (Node x in a)
            {
                if (seen.Add(x.K)) o.Add(x);
            }
            return o;
        }

        public static Node Union(params Node[] xs)
        {
            List<Node> flat = new List<Node>();
            foreach (Node x in xs)
            {
                if (Same(x, Empty)) continue;
                if (x.T == "union") flat.AddRange(x.Items); else flat.Add(x);
            }
            List<Node> o = Dedupe(flat);
            if (o.Any(q => Same(q, Eps)))
            {
                // eps + r r* = r*      and      eps + (nullable) = (nullable)
                List<Node> m = new List<Node>();
                foreach (Node x in o)
                {
                    Node y = x;
                    if (x.T == "cat" && x.Items.Count == 2)
                    {
                        Node a = x.Items[0];
                        Node b = x.Items[1];
                        if (b.T == "star" && Same(b.R, a)) y = Star(a);
                        else if (a.T == "star" && Same(a.R, b)) y = Star(b);
                    }
                    m.Add(y);
                }
                o = Dedupe(m);
                if (o.Any(q => !Same(q, Eps) && Nullable(q)))
                    o = o.Where(q => !Same(q, Eps)).ToList();
            }
            if (o.Count == 0) return Empty;
            if (o.Count == 1) return o[0];
            Node n = new Node();
            n.T = "union";
            n.Items = o;
            return Mk(n);
        }

        // ---- statistics used by the Properties tab ----
        public static int CountSyms(Node r)
        {
            switch (r.T)
            {
                case "sym": return 1;
                case "union":
                case "cat": return r.Items.Sum(q => CountSyms(q));
                case "star": return CountSyms(r.R);
                default: return 0;
            }
        }

        public static int CountStars(Node r)
        {
            switch (r.T)
            {
                case "union":
                case "cat": return r.Items.Sum(q => CountStars(q));
                case "star": return 1 + CountStars(r.R);
                default: return 0;
            }
        }

        public static int CountUnions(Node r)
        {
            switch (r.T)
            {
                case "union": return r.Items.Count - 1 + r.Items.Sum(q => CountUnions(q));
                case "cat": return r.Items.Sum(q => CountUnions(q));
                case "star": return CountUnions(r.R);
                default: return 0;
            }
        }

        public static int StarHeight(Node r)
        {
            switch (r.T)
            {
                case "union":
                case "cat": return r.Items.Max(q => StarHeight(q));
                case "star": return 1 + StarHeight(r.R);
                default: return 0;
            }
        }

        public static string ToNetRe(Node r)
        {
            switch (r.T)
            {
                case "empty": return "(?!)";
                case "eps": return "";
                case "sym": return Regex.Escape(r.C);
                case "union": return "(?:" + string.Join("|", r.Items.Select(q => ToNetRe(q)).ToArray()) + ")";
                case "cat": return string.Concat(r.Items.Select(q => "(?:" + ToNetRe(q) + ")").ToArray());
                default: return "(?:" + ToNetRe(r.R) + ")*";
            }
        }
    }

    // ------------------------------------------------------------------
    //  DFA model, Arden solver, verifier
    // ------------------------------------------------------------------
    class Dfa
    {
        public List<string> States = new List<string>();
        public List<string> Alpha = new List<string>();
        public string Start;
        public HashSet<string> Acc = new HashSet<string>();
        public Dictionary<string, string> T = new Dictionary<string, string>();

        public static string Key(string s, string a) { return s + "|" + a; }

        public List<string> Reachable()
        {
            List<string> seen = new List<string>();
            seen.Add(Start);
            Stack<string> stack = new Stack<string>();
            stack.Push(Start);
            while (stack.Count > 0)
            {
                string s = stack.Pop();
                foreach (string a in Alpha)
                {
                    string t = T[Key(s, a)];
                    if (!seen.Contains(t)) { seen.Add(t); stack.Push(t); }
                }
            }
            return States.Where(q => seen.Contains(q)).ToList();
        }

        public bool Accepts(string w)
        {
            string s = Start;
            foreach (char ch in w) s = T[Key(s, ch.ToString())];
            return Acc.Contains(s);
        }
    }

    class Eq
    {
        public Dictionary<string, Node> Coef = new Dictionary<string, Node>();
        public Node Cst;
    }

    class SolveResult
    {
        public List<string> Log = new List<string>();
        public List<string> Initial = new List<string>();
        public Node Result;
    }

    class VerifyResult
    {
        public bool Ok;
        public int Checked;
        public int L;
        public string Bad;
    }

    static class Solver
    {
        const string Dot = "\u00b7";

        static string FmtEq(string v, Eq e, List<string> order)
        {
            List<string> terms = new List<string>();
            foreach (string x in order)
            {
                Node c;
                if (!e.Coef.TryGetValue(x, out c)) continue;
                if (Rx.Same(c, Rx.Empty)) continue;
                terms.Add(Rx.Same(c, Rx.Eps) ? x : Rx.ToStr(c, 1) + Dot + x);
            }
            if (!Rx.Same(e.Cst, Rx.Empty))
                terms.Add(e.Cst.T == "union" ? Rx.ToStr(e.Cst, 1) : Rx.ToStr(e.Cst));
            return v + " = " + (terms.Count > 0 ? string.Join(" + ", terms.ToArray()) : "\u2205");
        }

        static void DropEmpty(Dictionary<string, Node> coef)
        {
            foreach (string dk in coef.Keys.ToList())
            {
                if (Rx.Same(coef[dk], Rx.Empty)) coef.Remove(dk);
            }
        }

        public static SolveResult Solve(Dfa d)
        {
            SolveResult res = new SolveResult();
            List<string> states = d.Reachable();
            Dictionary<string, Eq> eqs = new Dictionary<string, Eq>();

            foreach (string s in states)
            {
                Eq e = new Eq();
                foreach (string a in d.Alpha)
                {
                    string t = d.T[Dfa.Key(s, a)];
                    Node cur;
                    if (!e.Coef.TryGetValue(t, out cur)) cur = Rx.Empty;
                    e.Coef[t] = Rx.Union(cur, Rx.Sym(a));
                }
                e.Cst = d.Acc.Contains(s) ? Rx.Eps : Rx.Empty;
                eqs[s] = e;
            }

            List<string> dropped = d.States.Where(q => !states.Contains(q)).ToList();
            if (dropped.Count > 0)
                res.Initial.Add("(Unreachable states ignored: " + string.Join(", ", dropped.ToArray()) + ")");
            foreach (string s in states) res.Initial.Add(FmtEq(s, eqs[s], states));

            List<string> order = states.Where(q => q != d.Start).ToList();
            order.Add(d.Start);

            res.Log.Add("STEP 0 - system of language equations");
            foreach (string s in states) res.Log.Add("    " + FmtEq(s, eqs[s], states));
            res.Log.Add("");

            int n = 0;
            foreach (string x in order)
            {
                n++;
                Eq eq = eqs[x];
                res.Log.Add("STEP " + n + " - eliminate " + x);

                Node R;
                if (!eq.Coef.TryGetValue(x, out R)) R = Rx.Empty;
                Dictionary<string, Node> nc = new Dictionary<string, Node>();
                Node ncst;
                if (!Rx.Same(R, Rx.Empty))
                {
                    Node Rs = Rx.Star(R);
                    res.Log.Add("  Arden: " + x + " = (" + Rx.ToStr(R) + ")" + Dot + x + " + S   =>   "
                                + x + " = (" + Rx.ToStr(R) + ")*" + Dot + "S");
                    foreach (KeyValuePair<string, Node> kv in eq.Coef)
                    {
                        if (kv.Key != x) nc[kv.Key] = Rx.Cat(Rs, kv.Value);
                    }
                    ncst = Rx.Cat(Rs, eq.Cst);
                }
                else
                {
                    res.Log.Add("  " + x + " does not appear on its own right-hand side; no Arden step needed");
                    foreach (KeyValuePair<string, Node> kv in eq.Coef) nc[kv.Key] = kv.Value;
                    ncst = eq.Cst;
                }
                DropEmpty(nc);
                eq.Coef = nc;
                eq.Cst = ncst;
                res.Log.Add("  " + FmtEq(x, eq, states));

                if (n == order.Count) break;

                for (int i = n; i < order.Count; i++)
                {
                    string other = order[i];
                    Eq ek = eqs[other];
                    Node X;
                    if (!ek.Coef.TryGetValue(x, out X)) continue;
                    ek.Coef.Remove(x);
                    foreach (string v in states)
                    {
                        Node c;
                        if (!eq.Coef.TryGetValue(v, out c)) continue;
                        Node cur2;
                        if (!ek.Coef.TryGetValue(v, out cur2)) cur2 = Rx.Empty;
                        ek.Coef[v] = Rx.Union(cur2, Rx.Cat(X, c));
                    }
                    ek.Cst = Rx.Union(ek.Cst, Rx.Cat(X, eq.Cst));
                    DropEmpty(ek.Coef);
                    res.Log.Add("  substitute into " + other + ":  " + FmtEq(other, ek, states));
                }
                res.Log.Add("");
            }

            res.Result = eqs[order[order.Count - 1]].Cst;
            res.Log.Add("");
            res.Log.Add("RESULT:  L(" + d.Start + ") = " + Rx.ToStr(res.Result));
            return res;
        }

        // compare DFA and regex on every string up to a length bound
        public static VerifyResult Verify(Dfa d, Node rx)
        {
            Regex re = new Regex("\\A(?:" + Rx.ToNetRe(rx) + ")\\z");
            int total = 1;
            int L = 0;
            double next = d.Alpha.Count;
            while (L < 12 && total + next <= 40000)
            {
                total += (int)next;
                L++;
                next *= d.Alpha.Count;
            }
            VerifyResult vr = new VerifyResult();
            vr.Ok = true;
            vr.L = L;
            List<string> level = new List<string>();
            level.Add("");
            int count = 0;
            for (int len = 0; len <= L; len++)
            {
                foreach (string w in level)
                {
                    count++;
                    if (d.Accepts(w) != re.IsMatch(w))
                    {
                        vr.Ok = false;
                        vr.Bad = w;
                        vr.Checked = count;
                        return vr;
                    }
                }
                if (len < L)
                {
                    List<string> nl = new List<string>();
                    foreach (string w2 in level)
                        foreach (string a in d.Alpha) nl.Add(w2 + a);
                    level = nl;
                }
            }
            vr.Checked = count;
            return vr;
        }
    }

    // ------------------------------------------------------------------
    //  Colour palette
    // ------------------------------------------------------------------
    static class Pal
    {
        public static readonly Color Ink = Color.FromArgb(30, 35, 48);
        public static readonly Color Muted = Color.FromArgb(100, 108, 130);
        public static readonly Color Gray = Color.FromArgb(150, 156, 170);
        public static readonly Color Slate = Color.FromArgb(71, 85, 105);
        public static readonly Color Indigo = Color.FromArgb(67, 56, 202);
        public static readonly Color Violet = Color.FromArgb(124, 58, 237);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color Teal = Color.FromArgb(13, 148, 136);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color Orange = Color.FromArgb(234, 88, 12);
        public static readonly Color Pink = Color.FromArgb(219, 39, 119);

        // one colour per alphabet symbol (cycles after 8)
        static readonly Color[] SymColors = new Color[]
        {
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(219, 39, 119),
            Color.FromArgb(13, 148, 136),
            Color.FromArgb(234, 88, 12),
            Color.FromArgb(124, 58, 237),
            Color.FromArgb(22, 163, 74),
            Color.FromArgb(161, 98, 7),
            Color.FromArgb(8, 145, 178)
        };

        public static Color SymColor(int i)
        {
            if (i < 0) i = 0;
            return SymColors[i % SymColors.Length];
        }

        public static Color RoleColor(StateInfo s)
        {
            if (!s.Reach) return Gray;
            if (s.Dead) return Red;
            if (s.Start) return Green;
            if (s.Acc) return Amber;
            return Blue;
        }
    }

    // ------------------------------------------------------------------
    //  Built-in examples
    // ------------------------------------------------------------------
    class Example
    {
        public string Name, States, Alpha, Start, Acc, Test;
        public string[] Trans;

        public static Example Make(string name, string states, string alpha, string start, string acc, string test, params string[] tr)
        {
            Example e = new Example();
            e.Name = name; e.States = states; e.Alpha = alpha; e.Start = start; e.Acc = acc; e.Test = test; e.Trans = tr;
            return e;
        }

        public static List<Example> All()
        {
            List<Example> l = new List<Example>();
            l.Add(Make("Even number of 0s", "q0,q1", "0,1", "q0", "q0", "1001",
                "q0|0|q1", "q0|1|q0", "q1|0|q0", "q1|1|q1"));
            l.Add(Make("Strings ending in \"ab\"", "q0,q1,q2", "a,b", "q0", "q2", "baab",
                "q0|a|q1", "q0|b|q0", "q1|a|q1", "q1|b|q2", "q2|a|q1", "q2|b|q0"));
            l.Add(Make("No two consecutive 1s", "q0,q1,q2", "0,1", "q0", "q0,q1", "1011",
                "q0|0|q0", "q0|1|q1", "q1|0|q0", "q1|1|q2", "q2|0|q2", "q2|1|q2"));
            l.Add(Make("Binary divisible by 3", "r0,r1,r2", "0,1", "r0", "r0", "110",
                "r0|0|r0", "r0|1|r1", "r1|0|r2", "r1|1|r0", "r2|0|r1", "r2|1|r2"));
            l.Add(Make("Contains substring 101", "s0,s1,s2,s3", "0,1", "s0", "s3", "11010",
                "s0|0|s0", "s0|1|s1", "s1|0|s2", "s1|1|s1", "s2|0|s0", "s2|1|s3", "s3|0|s3", "s3|1|s3"));
            l.Add(Make("Even 0s AND even 1s", "A,B,C,D", "0,1", "A", "A", "0110",
                "A|0|B", "A|1|C", "B|0|A", "B|1|D", "C|0|D", "C|1|A", "D|0|C", "D|1|B"));
            l.Add(Make("Starts with a (has trap state)", "q0,q1,dead", "a,b", "q0", "q1", "bab",
                "q0|a|q1", "q0|b|dead", "q1|a|q1", "q1|b|q1", "dead|a|dead", "dead|b|dead"));
            l.Add(Make("Contains \"ab\" over {a,b,c}", "s0,s1,s2", "a,b,c", "s0", "s2", "cabc",
                "s0|a|s1", "s0|b|s0", "s0|c|s0", "s1|a|s1", "s1|b|s2", "s1|c|s0",
                "s2|a|s2", "s2|b|s2", "s2|c|s2"));
            l.Add(Make("Not minimal + unreachable state", "p0,p1,p2,p3,u", "0,1", "p0", "p0,p2", "0110",
                "p0|0|p1", "p0|1|p2", "p1|0|p2", "p1|1|p3", "p2|0|p3", "p2|1|p0", "p3|0|p0", "p3|1|p1",
                "u|0|p0", "u|1|u"));
            return l;
        }
    }

    // ------------------------------------------------------------------
    //  Structural analysis of a DFA (the "properties")
    // ------------------------------------------------------------------
    class StateInfo
    {
        public string Name;
        public bool Start, Acc, Reach, Dead;
        public int In, Out, Loops, Cls = -1, Scc = -1;

        public string Role()
        {
            List<string> r = new List<string>();
            if (Start) r.Add("start");
            if (Acc) r.Add("accepting");
            if (!Reach) r.Add("unreachable");
            else if (Dead) r.Add("dead");
            if (r.Count == 0) r.Add("normal");
            return string.Join(" + ", r.ToArray());
        }
    }

    // Tarjan strongly-connected components
    class Tj
    {
        public int[,] Nx;
        public int M;
        public int[] Idx, Low, Comp;
        public bool[] OnS;
        public Stack<int> S = new Stack<int>();
        public int Counter, NComp;

        public Tj(int[,] nx, int n, int m)
        {
            Nx = nx; M = m;
            Idx = new int[n]; Low = new int[n]; Comp = new int[n]; OnS = new bool[n];
            for (int i = 0; i < n; i++) { Idx[i] = -1; Comp[i] = -1; }
        }

        public void Go(int v)
        {
            Idx[v] = Counter; Low[v] = Counter; Counter++;
            S.Push(v); OnS[v] = true;
            for (int j = 0; j < M; j++)
            {
                int w = Nx[v, j];
                if (Idx[w] < 0) { Go(w); Low[v] = Math.Min(Low[v], Low[w]); }
                else if (OnS[w]) Low[v] = Math.Min(Low[v], Idx[w]);
            }
            if (Low[v] == Idx[v])
            {
                int z;
                do { z = S.Pop(); OnS[z] = false; Comp[z] = NComp; } while (z != v);
                NComp++;
            }
        }
    }

    class Analysis
    {
        public Dfa D;
        public List<StateInfo> Info = new List<StateInfo>();
        public int Transitions, MinStates, SccCount, ReachCount;
        public bool Empty, AcceptsEps, Finite, StronglyConnected;
        public string Shortest, Longest;
        public List<string> Samples = new List<string>();
        public long[] Counts;
        public List<List<string>> Groups = new List<List<string>>();

        static bool HasCycle(int v, int[,] nx, int m, bool[] u, int[] col)
        {
            col[v] = 1;
            for (int j = 0; j < m; j++)
            {
                int y = nx[v, j];
                if (!u[y]) continue;
                if (col[y] == 1) return true;
                if (col[y] == 0 && HasCycle(y, nx, m, u, col)) return true;
            }
            col[v] = 2;
            return false;
        }

        static int LongestFrom(int v, int[,] nx, int m, bool[] u, bool[] acc, int[] memo, int[] pick)
        {
            if (memo[v] != -2) return memo[v];
            int best = acc[v] ? 0 : -1;
            pick[v] = -1;
            for (int j = 0; j < m; j++)
            {
                int y = nx[v, j];
                if (!u[y]) continue;
                int r = LongestFrom(y, nx, m, u, acc, memo, pick);
                if (r >= 0 && r + 1 > best) { best = r + 1; pick[v] = j; }
            }
            memo[v] = best;
            return best;
        }

        public static Analysis Compute(Dfa d)
        {
            Analysis a = new Analysis();
            a.D = d;
            int n = d.States.Count;
            int m = d.Alpha.Count;
            int s0 = d.States.IndexOf(d.Start);
            int[,] nx = new int[n, m];
            bool[] acc = new bool[n];
            for (int i = 0; i < n; i++)
            {
                acc[i] = d.Acc.Contains(d.States[i]);
                for (int j = 0; j < m; j++)
                    nx[i, j] = d.States.IndexOf(d.T[Dfa.Key(d.States[i], d.Alpha[j])]);
            }
            a.Transitions = n * m;

            // reachable states
            bool[] reach = new bool[n];
            Stack<int> st = new Stack<int>();
            reach[s0] = true; st.Push(s0);
            while (st.Count > 0)
            {
                int x = st.Pop();
                for (int j = 0; j < m; j++)
                {
                    int y = nx[x, j];
                    if (!reach[y]) { reach[y] = true; st.Push(y); }
                }
            }
            for (int i = 0; i < n; i++) if (reach[i]) a.ReachCount++;

            // live states = can still reach an accepting state
            bool[] live = new bool[n];
            for (int i = 0; i < n; i++) live[i] = acc[i];
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < n; i++)
                {
                    if (live[i]) continue;
                    for (int j = 0; j < m; j++)
                    {
                        if (live[nx[i, j]]) { live[i] = true; changed = true; break; }
                    }
                }
            }
            bool[] useful = new bool[n];
            for (int i = 0; i < n; i++) useful[i] = reach[i] && live[i];

            a.Empty = !live[s0];
            a.AcceptsEps = acc[s0];

            // finite or infinite
            if (a.Empty) a.Finite = true;
            else
            {
                int[] col = new int[n];
                a.Finite = !HasCycle(s0, nx, m, useful, col);
            }

            // shortest accepted string (BFS over states)
            if (!a.Empty)
            {
                int[] par = new int[n];
                int[] parSym = new int[n];
                bool[] seen = new bool[n];
                Queue<int> q = new Queue<int>();
                q.Enqueue(s0); seen[s0] = true; par[s0] = -1;
                int goal = -1;
                while (q.Count > 0)
                {
                    int x = q.Dequeue();
                    if (acc[x]) { goal = x; break; }
                    for (int j = 0; j < m; j++)
                    {
                        int y = nx[x, j];
                        if (!seen[y]) { seen[y] = true; par[y] = x; parSym[y] = j; q.Enqueue(y); }
                    }
                }
                List<string> rev = new List<string>();
                int cur = goal;
                while (cur != s0) { rev.Add(d.Alpha[parSym[cur]]); cur = par[cur]; }
                rev.Reverse();
                a.Shortest = string.Concat(rev.ToArray());

                // first few accepted strings in shortlex order
                List<string> strs = new List<string>();
                List<int> sts = new List<int>();
                strs.Add(""); sts.Add(s0);
                int head = 0;
                while (head < strs.Count && a.Samples.Count < 10 && strs.Count < 20000)
                {
                    string w = strs[head];
                    int qs = sts[head];
                    head++;
                    if (acc[qs]) a.Samples.Add(w);
                    if (w.Length >= 40) continue;
                    for (int j = 0; j < m; j++)
                    {
                        int y = nx[qs, j];
                        if (!live[y]) continue;
                        strs.Add(w + d.Alpha[j]);
                        sts.Add(y);
                    }
                }

                // longest accepted string (only if the language is finite)
                if (a.Finite)
                {
                    int[] memo = new int[n];
                    int[] pick = new int[n];
                    for (int i = 0; i < n; i++) { memo[i] = -2; pick[i] = -1; }
                    LongestFrom(s0, nx, m, useful, acc, memo, pick);
                    List<string> lw = new List<string>();
                    int v = s0;
                    while (pick[v] != -1) { lw.Add(d.Alpha[pick[v]]); v = nx[v, pick[v]]; }
                    a.Longest = string.Concat(lw.ToArray());
                }
            }

            // number of accepted strings for each length 0..10
            int N = 10;
            a.Counts = new long[N + 1];
            long[] cnt = new long[n];
            cnt[s0] = 1;
            for (int len = 0; len <= N; len++)
            {
                long tot = 0;
                for (int i = 0; i < n; i++) if (acc[i]) tot += cnt[i];
                a.Counts[len] = tot;
                if (len < N)
                {
                    long[] nxt = new long[n];
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < m; j++) nxt[nx[i, j]] += cnt[i];
                    cnt = nxt;
                }
            }

            // Moore partition refinement on the reachable states
            int[] cls = new int[n];
            HashSet<int> firstSet = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                cls[i] = reach[i] ? (acc[i] ? 1 : 0) : -1;
                if (reach[i]) firstSet.Add(cls[i]);
            }
            int count = firstSet.Count;
            while (true)
            {
                Dictionary<string, int> map = new Dictionary<string, int>();
                int[] nc = new int[n];
                for (int i = 0; i < n; i++)
                {
                    if (!reach[i]) { nc[i] = -1; continue; }
                    StringBuilder sb = new StringBuilder();
                    sb.Append(cls[i]);
                    for (int j = 0; j < m; j++) { sb.Append(','); sb.Append(cls[nx[i, j]]); }
                    string key = sb.ToString();
                    int id;
                    if (!map.TryGetValue(key, out id)) { id = map.Count; map[key] = id; }
                    nc[i] = id;
                }
                bool same = (map.Count == count);
                cls = nc;
                count = map.Count;
                if (same) break;
            }
            a.MinStates = count;
            Dictionary<int, List<string>> grp = new Dictionary<int, List<string>>();
            for (int i = 0; i < n; i++)
            {
                if (!reach[i]) continue;
                if (!grp.ContainsKey(cls[i])) grp[cls[i]] = new List<string>();
                grp[cls[i]].Add(d.States[i]);
            }
            foreach (KeyValuePair<int, List<string>> kv in grp)
                if (kv.Value.Count > 1) a.Groups.Add(kv.Value);

            // strongly connected components
            Tj tj = new Tj(nx, n, m);
            tj.Go(s0);
            a.SccCount = tj.NComp;
            a.StronglyConnected = (tj.NComp == 1) && a.ReachCount == tj.Counter;

            // per-state info
            int[] inDeg = new int[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++) inDeg[nx[i, j]]++;
            for (int i = 0; i < n; i++)
            {
                StateInfo si = new StateInfo();
                si.Name = d.States[i];
                si.Start = (i == s0);
                si.Acc = acc[i];
                si.Reach = reach[i];
                si.Dead = !live[i];
                si.Cls = cls[i];
                si.Scc = tj.Comp[i];
                si.In = inDeg[i];
                HashSet<int> outs = new HashSet<int>();
                for (int j = 0; j < m; j++)
                {
                    outs.Add(nx[i, j]);
                    if (nx[i, j] == i) si.Loops++;
                }
                si.Out = outs.Count;
                a.Info.Add(si);
            }
            return a;
        }
    }

    // ------------------------------------------------------------------
    //  State diagram panel (coloured by role, edges coloured by symbol)
    // ------------------------------------------------------------------
    class DiagramPanel : Panel
    {
        public Dfa Model;
        public Analysis Info;
        public List<int> Path;      // state indices visited by the test string
        public bool PathOk;

        public DiagramPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.BackColor = Color.White;
        }

        static PointF Off(PointF p, float ux, float uy, float vx, float vy, float a, float b)
        {
            return new PointF(p.X + ux * a + vx * b, p.Y + uy * a + vy * b);
        }

        static PointF Trim(PointF from, PointF toward, float r)
        {
            float dx = toward.X - from.X;
            float dy = toward.Y - from.Y;
            float dd = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dd < 0.001f) dd = 1f;
            return new PointF(from.X + dx / dd * r, from.Y + dy / dd * r);
        }

        static void NodeColors(StateInfo si, out Color fill, out Color line)
        {
            if (!si.Reach) { fill = Color.FromArgb(235, 237, 242); line = Pal.Gray; }
            else if (si.Dead) { fill = Color.FromArgb(254, 226, 226); line = Pal.Red; }
            else if (si.Start) { fill = Color.FromArgb(209, 250, 229); line = Pal.Green; }
            else if (si.Acc) { fill = Color.FromArgb(254, 243, 199); line = Pal.Amber; }
            else { fill = Color.FromArgb(219, 234, 254); line = Pal.Blue; }
        }

        // label made of coloured symbols, e.g.  a , b  on a white chip
        static void DrawLabel(Graphics g, List<string> syms, List<string> alpha, PointF c, Font f, bool hot)
        {
            StringFormat tf = StringFormat.GenericTypographic;
            float commaW = g.MeasureString(",", f, 200, tf).Width + 3f;
            float[] w = new float[syms.Count];
            float total = 0f;
            for (int k = 0; k < syms.Count; k++)
            {
                w[k] = g.MeasureString(syms[k], f, 200, tf).Width;
                total += w[k];
                if (k > 0) total += commaW;
            }
            float h = f.GetHeight(g);
            RectangleF bg = new RectangleF(c.X - total / 2f - 4f, c.Y - h / 2f - 1f, total + 8f, h + 2f);
            using (Brush bb = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.FillRectangle(bb, bg);
            using (Pen bp = new Pen(hot ? Pal.Orange : Color.FromArgb(205, 210, 228), 1f))
                g.DrawRectangle(bp, bg.X, bg.Y, bg.Width, bg.Height);
            float x = c.X - total / 2f;
            for (int k = 0; k < syms.Count; k++)
            {
                if (k > 0)
                {
                    using (Brush gb = new SolidBrush(Pal.Muted)) g.DrawString(",", f, gb, x + 1f, c.Y - h / 2f, tf);
                    x += commaW;
                }
                using (Brush sb = new SolidBrush(Pal.SymColor(alpha.IndexOf(syms[k]))))
                    g.DrawString(syms[k], f, sb, x, c.Y - h / 2f, tf);
                x += w[k];
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle rc = ClientRectangle;
            if (rc.Width < 4 || rc.Height < 4) return;
            using (LinearGradientBrush bgb = new LinearGradientBrush(rc, Color.FromArgb(251, 252, 255), Color.FromArgb(234, 239, 255), 90f))
                g.FillRectangle(bgb, rc);

            Dfa d = Model;
            Analysis info = Info;
            if (d == null || info == null) return;
            int n = d.States.Count;
            float W = ClientSize.Width;
            float H = ClientSize.Height;
            if (W < 80 || H < 80 || n == 0) return;

            float cx = W / 2f, cy = H / 2f - 6f, r = 24f;
            float R = n > 1 ? Math.Min(W, H) * 0.29f : 0f;
            PointF[] pos = new PointF[n];
            double[] ang = new double[n];
            for (int i = 0; i < n; i++)
            {
                ang[i] = -Math.PI / 2 + 2 * Math.PI * i / n;
                pos[i] = new PointF(cx + R * (float)Math.Cos(ang[i]), cy + R * (float)Math.Sin(ang[i]));
            }

            Dictionary<int, List<string>> edges = new Dictionary<int, List<string>>();
            for (int i = 0; i < n; i++)
            {
                foreach (string a in d.Alpha)
                {
                    int j = d.States.IndexOf(d.T[Dfa.Key(d.States[i], a)]);
                    int key = i * n + j;
                    if (!edges.ContainsKey(key)) edges[key] = new List<string>();
                    edges[key].Add(a);
                }
            }

            // test-string path
            HashSet<int> hot = new HashSet<int>();
            HashSet<int> visited = new HashSet<int>();
            int finalState = -1;
            if (Path != null && Path.Count > 0)
            {
                for (int k = 0; k < Path.Count; k++)
                {
                    visited.Add(Path[k]);
                    if (k + 1 < Path.Count) hot.Add(Path[k] * n + Path[k + 1]);
                }
                finalState = Path[Path.Count - 1];
            }

            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;

            using (Font bold = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (Font small = new Font("Segoe UI", 8.5f))
            using (Brush ink = new SolidBrush(Pal.Ink))
            using (Brush gray = new SolidBrush(Pal.Muted))
            {
                // edges: normal ones first, highlighted path on top
                List<int> keys = edges.Keys.OrderBy(k0 => hot.Contains(k0) ? 1 : 0).ToList();
                foreach (int key in keys)
                {
                    int i = key / n;
                    int j = key % n;
                    List<string> syms = edges[key];
                    bool isHot = hot.Contains(key);
                    bool faded = !info.Info[i].Reach;
                    Color ec = syms.Count == 1 ? Pal.SymColor(d.Alpha.IndexOf(syms[0])) : Pal.Slate;
                    if (isHot) ec = Pal.Orange;
                    if (faded) ec = Color.FromArgb(175, 180, 192);
                    PointF p = pos[i];
                    PointF q = pos[j];
                    using (Pen pen = new Pen(ec, isHot ? 3.2f : 1.8f))
                    {
                        if (faded) pen.DashStyle = DashStyle.Dash;
                        pen.CustomEndCap = isHot ? new AdjustableArrowCap(3f, 3.4f) : new AdjustableArrowCap(5f, 6f);
                        if (i == j)
                        {
                            float ux = (float)Math.Cos(ang[i]);
                            float uy = (float)Math.Sin(ang[i]);
                            float vx = -uy, vy = ux;
                            g.DrawBezier(pen,
                                Off(p, ux, uy, vx, vy, r, -9f),
                                Off(p, ux, uy, vx, vy, r + 46f, -32f),
                                Off(p, ux, uy, vx, vy, r + 46f, 32f),
                                Off(p, ux, uy, vx, vy, r, 9f));
                            DrawLabel(g, syms, d.Alpha, Off(p, ux, uy, vx, vy, r + 50f, 0f), bold, isHot);
                            continue;
                        }
                        float dx = q.X - p.X, dy = q.Y - p.Y;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 0.001f) dist = 1f;
                        float nx = -dy / dist, ny = dx / dist;
                        float off = edges.ContainsKey(j * n + i) ? 34f : 0f;
                        PointF m = new PointF((p.X + q.X) / 2f + nx * off, (p.Y + q.Y) / 2f + ny * off);
                        PointF s0 = Trim(p, m, r);
                        PointF e0 = Trim(q, m, r + (isHot ? 3f : 0f));
                        PointF c1 = new PointF(s0.X + 2f / 3f * (m.X - s0.X), s0.Y + 2f / 3f * (m.Y - s0.Y));
                        PointF c2 = new PointF(e0.X + 2f / 3f * (m.X - e0.X), e0.Y + 2f / 3f * (m.Y - e0.Y));
                        g.DrawBezier(pen, s0, c1, c2, e0);
                        float side = off > 0 ? 14f : -14f;
                        float lx = 0.25f * s0.X + 0.5f * m.X + 0.25f * e0.X + nx * side;
                        float ly = 0.25f * s0.Y + 0.5f * m.Y + 0.25f * e0.Y + ny * side;
                        DrawLabel(g, syms, d.Alpha, new PointF(lx, ly), bold, isHot);
                    }
                }

                // nodes
                for (int i = 0; i < n; i++)
                {
                    StateInfo si = info.Info[i];
                    PointF p = pos[i];
                    Color fill, line;
                    NodeColors(si, out fill, out line);

                    using (Brush sh = new SolidBrush(Color.FromArgb(38, 20, 20, 60)))
                        g.FillEllipse(sh, p.X - r + 2f, p.Y - r + 4f, 2 * r, 2 * r);
                    using (Brush fb = new SolidBrush(fill))
                        g.FillEllipse(fb, p.X - r, p.Y - r, 2 * r, 2 * r);

                    if (visited.Contains(i))
                    {
                        Color ring = Pal.Orange;
                        if (i == finalState) ring = PathOk ? Pal.Green : Pal.Red;
                        using (Pen vp = new Pen(ring, 3.4f))
                            g.DrawEllipse(vp, p.X - r - 5f, p.Y - r - 5f, 2 * r + 10f, 2 * r + 10f);
                    }

                    using (Pen np = new Pen(line, si.Start ? 3f : 2.2f))
                    {
                        if (!si.Reach) np.DashStyle = DashStyle.Dash;
                        g.DrawEllipse(np, p.X - r, p.Y - r, 2 * r, 2 * r);
                    }
                    if (si.Acc)
                    {
                        using (Pen ip = new Pen(si.Reach ? Pal.Amber : Pal.Gray, 2.2f))
                            g.DrawEllipse(ip, p.X - r + 5f, p.Y - r + 5f, 2 * r - 10f, 2 * r - 10f);
                    }
                    g.DrawString(si.Name, bold, ink, p.X, p.Y, sf);
                }

                // start arrow
                int sidx = d.States.IndexOf(d.Start);
                if (sidx >= 0)
                {
                    PointF sp = pos[sidx];
                    using (Pen ap = new Pen(Pal.Green, 2.6f))
                    {
                        ap.CustomEndCap = new AdjustableArrowCap(4f, 5f);
                        g.DrawLine(ap, sp.X - r - 46f, sp.Y, sp.X - r - 1f, sp.Y);
                    }
                    using (Brush gb = new SolidBrush(Pal.Green))
                        g.DrawString("start", small, gb, sp.X - r - 30f, sp.Y - 16f, sf);
                }

                // legend: symbol colours (top-left)
                float lxs = 12f;
                g.DrawString("Symbols:", small, gray, lxs, 8f);
                lxs += 58f;
                for (int k = 0; k < d.Alpha.Count; k++)
                {
                    using (Brush sb = new SolidBrush(Pal.SymColor(k)))
                    {
                        g.FillRectangle(sb, lxs, 14f, 16f, 4f);
                        g.DrawString(d.Alpha[k], small, sb, lxs + 18f, 8f);
                    }
                    lxs += 38f;
                }

                // legend: state roles (bottom-left)
                string[] names = new string[] { "start", "accepting (inner ring)", "dead / trap", "unreachable", "normal", "test path" };
                Color[] cols = new Color[] { Pal.Green, Pal.Amber, Pal.Red, Pal.Gray, Pal.Blue, Pal.Orange };
                float lx0 = 12f, ly0 = H - 24f;
                for (int k = 0; k < names.Length; k++)
                {
                    float tw = g.MeasureString(names[k], small).Width;
                    if (lx0 + tw + 30f > W - 8f) { lx0 = 12f; ly0 -= 20f; }
                    using (Brush db = new SolidBrush(cols[k])) g.FillEllipse(db, lx0, ly0 + 3f, 11f, 11f);
                    g.DrawString(names[k], small, gray, lx0 + 15f, ly0);
                    lx0 += tw + 34f;
                }
            }
            sf.Dispose();
        }
    }

    // batches neighbouring characters of equal colour/font into one RichTextBox write
    class Chunker
    {
        RichTextBox r;
        StringBuilder sb = new StringBuilder();
        Color c;
        Font f;

        public Chunker(RichTextBox box) { r = box; }

        public void Add(string s, Color col, Font font)
        {
            if (sb.Length > 0 && (col != c || !object.ReferenceEquals(font, f))) Flush();
            c = col; f = font;
            sb.Append(s);
        }

        public void Flush()
        {
            if (sb.Length == 0) return;
            MainForm.Put(r, sb.ToString(), c, f);
            sb.Length = 0;
        }
    }

    // ------------------------------------------------------------------
    //  Main window
    // ------------------------------------------------------------------
    class MainForm : Form
    {
        ComboBox cbExample, cbStart;
        TextBox tbStates, tbAlpha, tbTest;
        RichTextBox rtEq, rtSteps, rtProps, rtResult;
        FlowLayoutPanel accPanel;
        Panel tblHost;
        Label lblErr, lblVerify, lblVerdict, lblTrace;
        DiagramPanel diagram;
        Button btnCopy, btnSave, btnMin, btnRand;
        TabControl tabs;
        Font fMono, fMonoB, fRes, fResB, fTab;
        Random rnd = new Random();
        Dictionary<string, string> mem = new Dictionary<string, string>();
        Dictionary<string, ComboBox> cells = new Dictionary<string, ComboBox>();
        List<Example> examples = Example.All();
        bool loading = false;

        Dfa cur;
        Analysis curA;
        string resultText = "";
        List<string> stateOrder = new List<string>();
        List<string> alphaList = new List<string>();

        static readonly Color[] TabColors = new Color[] { Pal.Indigo, Pal.Violet, Pal.Teal, Pal.Pink };

        public MainForm()
        {
            this.Text = "Arden's Theorem Solver - DFA to Regular Expression";
            this.Width = 1300;
            this.Height = 880;
            this.MinimumSize = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f);
            this.BackColor = Color.FromArgb(244, 246, 252);

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            fMono = new Font("Consolas", 10.5f);
            fMonoB = new Font("Consolas", 10.5f, FontStyle.Bold);
            fRes = new Font("Consolas", 13f);
            fResB = new Font("Consolas", 13f, FontStyle.Bold);
            fTab = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            // ---------------- outer layout: banner + content ----------------
            TableLayoutPanel outer = new TableLayoutPanel();
            outer.Dock = DockStyle.Fill;
            outer.ColumnCount = 1;
            outer.RowCount = 2;
            outer.Margin = Padding.Empty;
            outer.Padding = Padding.Empty;
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            this.Controls.Add(outer);

            Panel hdr = new Panel();
            hdr.Dock = DockStyle.Fill;
            hdr.Margin = Padding.Empty;
            hdr.Paint += OnPaintHeader;
            hdr.Resize += delegate (object s0, EventArgs e0) { hdr.Invalidate(); };
            outer.Controls.Add(hdr, 0, 0);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            outer.Controls.Add(root, 0, 1);

            // ---------------- left panel ----------------
            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.TopDown;
            left.WrapContents = false;
            left.AutoScroll = true;
            left.Padding = new Padding(12);
            left.BackColor = Color.FromArgb(238, 240, 252);
            root.Controls.Add(left, 0, 0);

            Label title = new Label();
            title.Text = "DFA definition";
            title.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
            title.ForeColor = Pal.Indigo;
            title.AutoSize = true;
            left.Controls.Add(title);

            left.Controls.Add(MakeLabel("Example"));
            cbExample = new ComboBox();
            cbExample.DropDownStyle = ComboBoxStyle.DropDownList;
            cbExample.Width = 335;
            foreach (Example ex in examples) cbExample.Items.Add(ex.Name);
            cbExample.SelectedIndex = 1;
            left.Controls.Add(cbExample);

            left.Controls.Add(MakeLabel("States (comma separated)"));
            tbStates = new TextBox();
            tbStates.Width = 335;
            left.Controls.Add(tbStates);

            left.Controls.Add(MakeLabel("Alphabet (single characters, comma separated)"));
            tbAlpha = new TextBox();
            tbAlpha.Width = 335;
            left.Controls.Add(tbAlpha);

            left.Controls.Add(MakeLabel("Start state"));
            cbStart = new ComboBox();
            cbStart.DropDownStyle = ComboBoxStyle.DropDownList;
            cbStart.Width = 335;
            left.Controls.Add(cbStart);

            left.Controls.Add(MakeLabel("Accepting states (tick)"));
            accPanel = new FlowLayoutPanel();
            accPanel.Width = 335;
            accPanel.AutoSize = true;
            accPanel.WrapContents = true;
            accPanel.BackColor = Color.FromArgb(254, 249, 231);
            left.Controls.Add(accPanel);

            left.Controls.Add(MakeLabel("Transitions   (rows = state, columns = symbol)"));
            tblHost = new Panel();
            tblHost.AutoSize = true;
            tblHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            left.Controls.Add(tblHost);

            FlowLayoutPanel btnRow = new FlowLayoutPanel();
            btnRow.AutoSize = true;
            btnRow.WrapContents = false;
            btnRow.Margin = new Padding(3, 12, 3, 3);
            btnRand = MakeButton("Random DFA", Pal.Teal, 160);
            btnMin = MakeButton("Minimize DFA", Pal.Violet, 168);
            btnRand.Click += OnRandom;
            btnMin.Click += OnMinimize;
            btnRow.Controls.Add(btnRand);
            btnRow.Controls.Add(btnMin);
            left.Controls.Add(btnRow);

            lblErr = new Label();
            lblErr.AutoSize = true;
            lblErr.MaximumSize = new Size(335, 0);
            lblErr.ForeColor = Color.Firebrick;
            left.Controls.Add(lblErr);

            // ---------------- right panel ----------------
            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 4;
            right.Padding = new Padding(8);
            right.BackColor = Color.White;
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(right, 1, 0);

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(150, 32);
            tabs.Font = fTab;
            tabs.DrawItem += OnDrawTab;
            right.Controls.Add(tabs, 0, 0);

            TabPage tp1 = new TabPage("Equations");
            TabPage tp2 = new TabPage("Solution steps");
            TabPage tp3 = new TabPage("State diagram");
            TabPage tp4 = new TabPage("Properties");
            tabs.TabPages.Add(tp1);
            tabs.TabPages.Add(tp2);
            tabs.TabPages.Add(tp3);
            tabs.TabPages.Add(tp4);

            rtEq = MakeRich(Color.White);
            rtSteps = MakeRich(Color.FromArgb(252, 252, 255));
            rtProps = MakeRich(Color.White);
            tp1.Controls.Add(rtEq);
            tp2.Controls.Add(rtSteps);
            tp4.Controls.Add(rtProps);
            diagram = new DiagramPanel();
            diagram.Dock = DockStyle.Fill;
            tp3.Controls.Add(diagram);

            // test-string row
            FlowLayoutPanel testRow = new FlowLayoutPanel();
            testRow.Dock = DockStyle.Fill;
            testRow.AutoSize = true;
            testRow.WrapContents = true;
            testRow.BackColor = Color.FromArgb(255, 247, 237);
            testRow.Padding = new Padding(6, 4, 6, 4);
            testRow.Margin = new Padding(0, 8, 0, 0);
            Label lt = new Label();
            lt.Text = "Test a string:";
            lt.AutoSize = true;
            lt.Font = fTab;
            lt.ForeColor = Pal.Orange;
            lt.Margin = new Padding(3, 7, 3, 3);
            tbTest = new TextBox();
            tbTest.Width = 260;
            tbTest.Font = fMono;
            lblVerdict = new Label();
            lblVerdict.AutoSize = true;
            lblVerdict.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblVerdict.Margin = new Padding(12, 5, 3, 3);
            lblTrace = new Label();
            lblTrace.AutoSize = true;
            lblTrace.MaximumSize = new Size(780, 0);
            lblTrace.Font = fMono;
            lblTrace.ForeColor = Pal.Slate;
            lblTrace.Margin = new Padding(12, 8, 3, 3);
            testRow.Controls.Add(lt);
            testRow.Controls.Add(tbTest);
            testRow.Controls.Add(lblVerdict);
            testRow.Controls.Add(lblTrace);
            right.Controls.Add(testRow, 0, 1);

            // result row
            TableLayoutPanel resRow = new TableLayoutPanel();
            resRow.Dock = DockStyle.Fill;
            resRow.AutoSize = true;
            resRow.ColumnCount = 3;
            resRow.RowCount = 1;
            resRow.Margin = new Padding(0, 8, 0, 0);
            resRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            resRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            resRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rtResult = new RichTextBox();
            rtResult.ReadOnly = true;
            rtResult.Multiline = true;
            rtResult.WordWrap = true;
            rtResult.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtResult.DetectUrls = false;
            rtResult.BorderStyle = BorderStyle.FixedSingle;
            rtResult.Font = fRes;
            rtResult.BackColor = Color.FromArgb(238, 242, 255);
            rtResult.Height = 62;
            rtResult.Dock = DockStyle.Fill;
            btnCopy = MakeButton("Copy", Pal.Indigo, 90);
            btnCopy.Click += OnCopy;
            btnSave = MakeButton("Save report...", Pal.Green, 130);
            btnSave.Click += OnSave;
            resRow.Controls.Add(rtResult, 0, 0);
            resRow.Controls.Add(btnCopy, 1, 0);
            resRow.Controls.Add(btnSave, 2, 0);
            right.Controls.Add(resRow, 0, 2);

            lblVerify = new Label();
            lblVerify.AutoSize = true;
            lblVerify.Margin = new Padding(3, 6, 3, 3);
            right.Controls.Add(lblVerify, 0, 3);

            // ---------------- events ----------------
            cbExample.SelectedIndexChanged += OnExampleChanged;
            tbStates.TextChanged += OnStructureChanged;
            tbAlpha.TextChanged += OnStructureChanged;
            cbStart.SelectedIndexChanged += OnAnyChange;
            tbTest.TextChanged += delegate (object s1, EventArgs e1) { if (!loading) UpdateTest(); };

            LoadExample(examples[1]);
        }

        // ---------------- small UI helpers ----------------
        static Label MakeLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = Pal.Muted;
            l.Margin = new Padding(3, 12, 3, 2);
            return l;
        }

        static Button MakeButton(string text, Color back, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 34;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        static RichTextBox MakeRich(Color back)
        {
            RichTextBox t = new RichTextBox();
            t.ReadOnly = true;
            t.Multiline = true;
            t.WordWrap = false;
            t.DetectUrls = false;
            t.ScrollBars = RichTextBoxScrollBars.Both;
            t.BorderStyle = BorderStyle.None;
            t.Dock = DockStyle.Fill;
            t.BackColor = back;
            t.Font = new Font("Consolas", 10.5f);
            return t;
        }

        void OnPaintHeader(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            Graphics g = e.Graphics;
            Rectangle rc = p.ClientRectangle;
            if (rc.Width < 4 || rc.Height < 4) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (LinearGradientBrush b = new LinearGradientBrush(rc, Color.FromArgb(30, 27, 75), Color.FromArgb(109, 40, 217), 0f))
                g.FillRectangle(b, rc);
            using (Font f1 = new Font("Segoe UI", 16f, FontStyle.Bold))
                g.DrawString("Arden's Theorem Solver", f1, Brushes.White, 16f, 6f);
            using (Font f2 = new Font("Segoe UI", 9f))
            using (Brush sb = new SolidBrush(Color.FromArgb(199, 210, 254)))
                g.DrawString("DFA  to  regular expression, solved with Arden's Lemma", f2, sb, 18f, 38f);
            string formula = "X = RX + S   =>   X = R*S";
            using (Font f3 = new Font("Consolas", 11.5f, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(formula, f3);
                float x = rc.Width - sz.Width - 30f;
                using (Brush pb = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                    g.FillRectangle(pb, x - 12f, 16f, sz.Width + 24f, 32f);
                g.DrawString(formula, f3, Brushes.White, x, 21f);
            }
        }

        void OnDrawTab(object sender, DrawItemEventArgs e)
        {
            TabControl tc = (TabControl)sender;
            bool sel = (e.Index == tc.SelectedIndex);
            Color acc = TabColors[e.Index % TabColors.Length];
            Rectangle r = e.Bounds;
            using (Brush bb = new SolidBrush(sel ? acc : Color.FromArgb(236, 238, 248)))
                e.Graphics.FillRectangle(bb, r);
            using (Brush tb = new SolidBrush(sel ? Color.White : acc))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(tc.TabPages[e.Index].Text, fTab, tb, r, sf);
            }
        }

        // ---------------- coloured text output ----------------
        public static void Put(RichTextBox r, string s, Color fg, Font f)
        {
            PutBg(r, s, fg, r.BackColor, f);
        }

        static void PutBg(RichTextBox r, string s, Color fg, Color bg, Font f)
        {
            r.SelectionStart = r.TextLength;
            r.SelectionLength = 0;
            r.SelectionColor = fg;
            r.SelectionBackColor = bg;
            r.SelectionFont = f;
            r.SelectedText = s;
        }

        // colours regex / equation text: states blue, symbols by colour, * red, + amber ...
        void PutRich(RichTextBox r, string s, Color baseColor, Font reg, Font bold, List<string> stNames)
        {
            Chunker ck = new Chunker(r);
            int i = 0;
            while (i < s.Length)
            {
                string hit = null;
                if (stNames != null && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
                {
                    foreach (string nm in stNames)
                    {
                        if (string.CompareOrdinal(s, i, nm, 0, nm.Length) == 0) { hit = nm; break; }
                    }
                }
                if (hit != null)
                {
                    ck.Add(hit, Pal.Blue, bold);
                    i += hit.Length;
                    continue;
                }
                char c = s[i];
                string cs = c.ToString();
                int ai = alphaList.IndexOf(cs);
                if (ai >= 0) ck.Add(cs, Pal.SymColor(ai), bold);
                else if (c == '*') ck.Add(cs, Pal.Red, bold);
                else if (c == '+') ck.Add(cs, Pal.Amber, bold);
                else if (c == '(' || c == ')') ck.Add(cs, Pal.Gray, bold);
                else if (c == '\u03b5' || c == '\u2205') ck.Add(cs, Pal.Violet, bold);
                else if (c == '\u00b7' || c == '=') ck.Add(cs, Pal.Muted, reg);
                else ck.Add(cs, baseColor, reg);
                i++;
            }
            ck.Flush();
        }

        void FillEq(SolveResult res)
        {
            RichTextBox r = rtEq;
            r.Clear();
            Put(r, "Language equations", Pal.Indigo, fMonoB);
            Put(r, "   Q = sum of a" + "\u00b7" + "Q_next, plus \u03b5 if the state is accepting\n\n", Pal.Muted, fMono);
            foreach (string line in res.Initial)
            {
                if (line.StartsWith("(")) Put(r, line, Pal.Muted, fMono);
                else PutRich(r, line, Pal.Ink, fMono, fMonoB, stateOrder);
                Put(r, "\n", Pal.Ink, fMono);
            }
            r.SelectionStart = 0; r.SelectionLength = 0; r.ScrollToCaret();
        }

        void FillSteps(SolveResult res)
        {
            RichTextBox r = rtSteps;
            r.Clear();
            foreach (string line in res.Log)
            {
                string t = line.TrimStart();
                if (line.StartsWith("STEP")) Put(r, line, Pal.Indigo, fMonoB);
                else if (line.StartsWith("RESULT"))
                {
                    int k = line.IndexOf(" = ");
                    Put(r, line.Substring(0, k + 3), Pal.Green, fMonoB);
                    PutRich(r, line.Substring(k + 3), Pal.Ink, fMono, fMonoB, null);
                }
                else if (t.StartsWith("Arden:")) Put(r, line, Pal.Violet, fMono);
                else if (t.StartsWith("substitute into"))
                {
                    int k = line.IndexOf(':');
                    Put(r, line.Substring(0, k + 1), Pal.Teal, fMonoB);
                    PutRich(r, line.Substring(k + 1), Pal.Ink, fMono, fMonoB, stateOrder);
                }
                else if (line.Contains("does not appear")) Put(r, line, Pal.Muted, fMono);
                else PutRich(r, line, Pal.Ink, fMono, fMonoB, stateOrder);
                Put(r, "\n", Pal.Ink, fMono);
            }
            r.SelectionStart = 0; r.SelectionLength = 0; r.ScrollToCaret();
        }

        void FillResult()
        {
            rtResult.Clear();
            Put(rtResult, "L = ", Pal.Muted, fRes);
            PutRich(rtResult, resultText, Pal.Ink, fRes, fResB, null);
        }

        // ---------------- properties tab ----------------
        void Head(RichTextBox r, string text, Color c)
        {
            if (r.TextLength > 0) Put(r, "\n", Pal.Ink, fMono);
            PutBg(r, " " + text + " ", Color.White, c, fMonoB);
            Put(r, "\n", Pal.Ink, fMono);
        }

        void Row(RichTextBox r, string label, string value, Color vc)
        {
            Put(r, "  " + label.PadRight(28), Pal.Muted, fMono);
            Put(r, value, vc, fMonoB);
            Put(r, "\n", Pal.Ink, fMono);
        }

        static string Disp(string w)
        {
            return w.Length == 0 ? "\u03b5" : w;
        }

        static string Join(IEnumerable<string> xs)
        {
            return string.Join(", ", xs.ToArray());
        }

        void FillProps(Analysis a, Node rx)
        {
            RichTextBox r = rtProps;
            r.Clear();
            Dfa d = a.D;
            int m = d.Alpha.Count;
            List<string> unreach = a.Info.Where(q => !q.Reach).Select(q => q.Name).ToList();
            List<string> dead = a.Info.Where(q => q.Reach && q.Dead).Select(q => q.Name).ToList();
            List<string> accs = a.Info.Where(q => q.Acc).Select(q => q.Name).ToList();
            int loops = a.Info.Sum(q => q.Loops);

            Head(r, "STRUCTURE", Pal.Blue);
            Row(r, "States |Q|", d.States.Count + "   (" + a.ReachCount + " reachable, " + unreach.Count + " unreachable)", Pal.Ink);
            Row(r, "Alphabet |\u03a3|", m + "   { " + Join(d.Alpha) + " }", Pal.Ink);
            Row(r, "Transitions", a.Transitions + "   (complete: every state has a move on every symbol)", Pal.Ink);
            Row(r, "Start state", d.Start, Pal.Green);
            Row(r, "Accepting states", accs.Count + (accs.Count > 0 ? "   { " + Join(accs) + " }" : ""), Pal.Amber);
            Row(r, "Dead (trap) states", dead.Count == 0 ? "none" : Join(dead), dead.Count == 0 ? Pal.Muted : Pal.Red);
            Row(r, "Unreachable states", unreach.Count == 0 ? "none" : Join(unreach), unreach.Count == 0 ? Pal.Muted : Pal.Gray);
            Row(r, "Self-loops", loops.ToString(), Pal.Ink);

            Head(r, "LANGUAGE", Pal.Pink);
            Row(r, "Language is", a.Empty ? "empty (accepts nothing)" : (a.Finite ? "finite" : "infinite"),
                a.Empty ? Pal.Red : (a.Finite ? Pal.Teal : Pal.Violet));
            Row(r, "Accepts the empty string \u03b5", a.AcceptsEps ? "yes" : "no", a.AcceptsEps ? Pal.Green : Pal.Muted);
            if (!a.Empty)
            {
                Row(r, "Shortest accepted string", Disp(a.Shortest) + "   (length " + a.Shortest.Length + ")", Pal.Ink);
                if (a.Finite && a.Longest != null)
                    Row(r, "Longest accepted string", Disp(a.Longest) + "   (length " + a.Longest.Length + ")", Pal.Ink);
                Row(r, "First accepted strings", Join(a.Samples.Select(q => Disp(q))) + (a.Samples.Count >= 10 ? ", ..." : ""), Pal.Ink);
            }
            Put(r, "\n  Accepted strings by length\n", Pal.Muted, fMono);
            for (int len = 0; len < a.Counts.Length; len++)
            {
                double total = Math.Pow(m, len);
                double share = a.Counts[len] / total;
                int filled = (int)Math.Round(share * 24.0);
                Put(r, ("    length " + len.ToString().PadLeft(2) + "  ").PadRight(15), Pal.Muted, fMono);
                Put(r, a.Counts[len].ToString("N0").PadLeft(10), Pal.Ink, fMonoB);
                Put(r, (" of " + ((long)total).ToString("N0")).PadRight(18), Pal.Muted, fMono);
                Put(r, new string('\u2588', filled), Pal.Teal, fMono);
                Put(r, new string('\u2591', 24 - filled), Color.FromArgb(200, 205, 220), fMono);
                Put(r, "  " + (share * 100.0).ToString("0.0") + "%\n", Pal.Muted, fMono);
            }

            Head(r, "MINIMALITY", Pal.Teal);
            if (a.MinStates == a.ReachCount)
                Row(r, "Minimal DFA?", unreach.Count == 0 ? "yes - no two states are equivalent"
                    : "yes, apart from the unreachable state(s)", Pal.Green);
            else
                Row(r, "Minimal DFA?", "no - an equivalent DFA needs only " + a.MinStates + " states (saves "
                    + (a.ReachCount - a.MinStates) + ")", Pal.Orange);
            foreach (List<string> grp in a.Groups)
                Row(r, "  equivalent states", "{ " + Join(grp) + " }", Pal.Violet);
            if (a.MinStates != a.ReachCount)
                Put(r, "  (press \"Minimize DFA\" on the left to load the minimal machine)\n", Pal.Muted, fMono);

            Head(r, "GRAPH", Pal.Violet);
            Row(r, "Strongly connected components", a.SccCount + "   (reachable part)", Pal.Ink);
            Row(r, "Strongly connected", a.StronglyConnected ? "yes" : "no", a.StronglyConnected ? Pal.Green : Pal.Muted);
            Put(r, "\n", Pal.Ink, fMono);
            Put(r, ("  " + "State").PadRight(12) + "Role".PadRight(26) + "In".PadLeft(4) + "Out".PadLeft(5)
                 + "Loops".PadLeft(7) + "Class".PadLeft(7) + "SCC".PadLeft(5) + "\n", Pal.Muted, fMonoB);
            foreach (StateInfo si in a.Info)
            {
                Color rc = Pal.RoleColor(si);
                Put(r, ("  " + si.Name).PadRight(12), rc, fMonoB);
                Put(r, si.Role().PadRight(26), rc, fMono);
                Put(r, si.In.ToString().PadLeft(4) + si.Out.ToString().PadLeft(5) + si.Loops.ToString().PadLeft(7)
                    + (si.Reach ? si.Cls.ToString() : "-").PadLeft(7) + (si.Reach ? si.Scc.ToString() : "-").PadLeft(5) + "\n",
                    Pal.Ink, fMono);
            }

            Head(r, "REGULAR EXPRESSION", Pal.Orange);
            Row(r, "Symbols in the expression", Rx.CountSyms(rx).ToString(), Pal.Ink);
            Row(r, "Kleene stars (*)", Rx.CountStars(rx).ToString(), Pal.Red);
            Row(r, "Union operators (+)", Rx.CountUnions(rx).ToString(), Pal.Amber);
            Row(r, "Star height", Rx.StarHeight(rx).ToString(), Pal.Ink);
            Row(r, "Length (characters)", resultText.Length.ToString(), Pal.Ink);

            r.SelectionStart = 0; r.SelectionLength = 0; r.ScrollToCaret();
        }

        // ---------------- test string ----------------
        void UpdateTest()
        {
            diagram.Path = null;
            lblVerdict.Text = "";
            lblTrace.Text = "";
            Dfa d = cur;
            if (d == null) { diagram.Invalidate(); return; }
            string w = tbTest.Text;
            List<int> path = new List<int>();
            int s = d.States.IndexOf(d.Start);
            path.Add(s);
            StringBuilder tr = new StringBuilder(d.Start);
            foreach (char ch in w)
            {
                string sym = ch.ToString();
                if (!d.Alpha.Contains(sym))
                {
                    lblVerdict.ForeColor = Pal.Orange;
                    lblVerdict.Text = "'" + sym + "' is not in the alphabet";
                    diagram.Invalidate();
                    return;
                }
                string t = d.T[Dfa.Key(d.States[s], sym)];
                s = d.States.IndexOf(t);
                path.Add(s);
                tr.Append("  -" + sym + "->  " + t);
            }
            bool ok = d.Acc.Contains(d.States[s]);
            lblVerdict.ForeColor = ok ? Pal.Green : Pal.Red;
            lblVerdict.Text = (w.Length == 0 ? "\u03b5   " : "") + (ok ? "ACCEPTED" : "REJECTED");
            lblTrace.Text = tr.ToString();
            diagram.Path = path;
            diagram.PathOk = ok;
            diagram.Invalidate();
        }

        static List<string> Parse(string text)
        {
            return text.Split(new char[] { ',', ';' })
                       .Select(q => q.Trim())
                       .Where(q => q.Length > 0)
                       .ToList();
        }

        static string Validate(List<string> states, List<string> alpha)
        {
            if (states.Count == 0) return "Enter at least one state.";
            if (states.Any(q => q.Contains("|"))) return "State names cannot contain the | character.";
            if (states.Distinct().Count() != states.Count) return "State names must be unique.";
            if (alpha.Count == 0) return "Enter at least one alphabet symbol.";
            if (alpha.Distinct().Count() != alpha.Count) return "Alphabet symbols must be unique.";
            if (alpha.Any(q => q.Length != 1)) return "Each alphabet symbol must be a single character.";
            return "";
        }

        HashSet<string> CheckedAccepting()
        {
            HashSet<string> h = new HashSet<string>();
            foreach (Control c in accPanel.Controls)
            {
                CheckBox cb = c as CheckBox;
                if (cb != null && cb.Checked) h.Add(cb.Text);
            }
            return h;
        }

        // ---------------- event handlers ----------------
        void OnAnyChange(object sender, EventArgs e)
        {
            if (loading) return;
            Run();
        }

        void OnStructureChanged(object sender, EventArgs e)
        {
            if (loading) return;
            Rebuild(null);
            Run();
        }

        void OnExampleChanged(object sender, EventArgs e)
        {
            if (loading) return;
            if (cbExample.SelectedIndex >= 0) LoadExample(examples[cbExample.SelectedIndex]);
        }

        void OnCopy(object sender, EventArgs e)
        {
            if (resultText.Length == 0) return;
            try
            {
                Clipboard.SetText(resultText);
                btnCopy.Text = "Copied!";
                System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
                tm.Interval = 1200;
                tm.Tick += delegate (object s2, EventArgs e2)
                {
                    btnCopy.Text = "Copy";
                    tm.Stop();
                    tm.Dispose();
                };
                tm.Start();
            }
            catch (Exception) { }
        }

        void OnSave(object sender, EventArgs e)
        {
            if (cur == null) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "Text report (*.txt)|*.txt";
                dlg.FileName = "ArdensSolver_report.txt";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ARDEN'S THEOREM SOLVER - REPORT");
                sb.AppendLine("================================");
                sb.AppendLine("States:    " + string.Join(", ", cur.States.ToArray()));
                sb.AppendLine("Alphabet:  " + string.Join(", ", cur.Alpha.ToArray()));
                sb.AppendLine("Start:     " + cur.Start);
                sb.AppendLine("Accepting: " + string.Join(", ", cur.States.Where(q => cur.Acc.Contains(q)).ToArray()));
                sb.AppendLine("Transitions:");
                foreach (string s in cur.States)
                    foreach (string a in cur.Alpha)
                        sb.AppendLine("  delta(" + s + "," + a + ") = " + cur.T[Dfa.Key(s, a)]);
                sb.AppendLine();
                sb.AppendLine("--- EQUATIONS ---");
                sb.AppendLine(rtEq.Text);
                sb.AppendLine("--- SOLUTION STEPS ---");
                sb.AppendLine(rtSteps.Text);
                sb.AppendLine("--- RESULT ---");
                sb.AppendLine("L = " + resultText);
                sb.AppendLine();
                sb.AppendLine("--- PROPERTIES ---");
                sb.AppendLine(rtProps.Text);
                string text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
                try { System.IO.File.WriteAllText(dlg.FileName, text, Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        // fill the editor with random transitions for the current states / alphabet
        void OnRandom(object sender, EventArgs e)
        {
            List<string> states = Parse(tbStates.Text);
            List<string> alpha = Parse(tbAlpha.Text);
            if (Validate(states, alpha).Length > 0) return;
            HashSet<string> acc = new HashSet<string>();
            foreach (string s in states)
            {
                if (rnd.NextDouble() < 0.4) acc.Add(s);
                foreach (string a in alpha) mem[Dfa.Key(s, a)] = states[rnd.Next(states.Count)];
            }
            if (acc.Count == 0) acc.Add(states[rnd.Next(states.Count)]);
            Rebuild(acc);
            loading = true;
            cbStart.SelectedIndex = 0;
            loading = false;
            Run();
        }

        // replace the DFA by its minimal equivalent (reachable part, equivalent states merged)
        void OnMinimize(object sender, EventArgs e)
        {
            Dfa d = ReadDfa();
            if (d == null) return;
            Analysis a = Analysis.Compute(d);
            if (a.MinStates == a.ReachCount && a.ReachCount == d.States.Count)
            {
                MessageBox.Show(this, "This DFA is already minimal.", "Minimize DFA", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // order classes: start class first, then in state order
            List<int> classOrder = new List<int>();
            int si = d.States.IndexOf(d.Start);
            classOrder.Add(a.Info[si].Cls);
            for (int i = 0; i < d.States.Count; i++)
                if (a.Info[i].Reach && !classOrder.Contains(a.Info[i].Cls)) classOrder.Add(a.Info[i].Cls);

            List<string> names = new List<string>();
            List<string> reps = new List<string>();
            for (int k = 0; k < classOrder.Count; k++)
            {
                names.Add("m" + k);
                for (int i = 0; i < d.States.Count; i++)
                {
                    if (a.Info[i].Reach && a.Info[i].Cls == classOrder[k]) { reps.Add(d.States[i]); break; }
                }
            }
            HashSet<string> acc = new HashSet<string>();
            Dictionary<string, string> newMem = new Dictionary<string, string>();
            for (int k = 0; k < names.Count; k++)
            {
                if (d.Acc.Contains(reps[k])) acc.Add(names[k]);
                foreach (string sym in d.Alpha)
                {
                    string tgt = d.T[Dfa.Key(reps[k], sym)];
                    int tc = a.Info[d.States.IndexOf(tgt)].Cls;
                    newMem[Dfa.Key(names[k], sym)] = names[classOrder.IndexOf(tc)];
                }
            }
            loading = true;
            tbStates.Text = string.Join(",", names.ToArray());
            mem.Clear();
            foreach (KeyValuePair<string, string> kv in newMem) mem[kv.Key] = kv.Value;
            loading = false;
            Rebuild(acc);
            loading = true;
            cbStart.SelectedIndex = 0;
            loading = false;
            Run();
        }

        // ---------------- build UI from the text boxes ----------------
        void Rebuild(HashSet<string> keepAcc)
        {
            loading = true;
            try
            {
                List<string> states = Parse(tbStates.Text);
                List<string> alpha = Parse(tbAlpha.Text);
                string err = Validate(states, alpha);
                lblErr.Text = err;
                if (err.Length > 0) return;

                HashSet<string> prevAcc = keepAcc != null ? keepAcc : CheckedAccepting();
                string prevStart = cbStart.Text;

                cbStart.Items.Clear();
                foreach (string s in states) cbStart.Items.Add(s);
                cbStart.SelectedIndex = states.Contains(prevStart) ? states.IndexOf(prevStart) : 0;

                accPanel.Controls.Clear();
                foreach (string s in states)
                {
                    CheckBox cb = new CheckBox();
                    cb.Text = s;
                    cb.AutoSize = true;
                    cb.ForeColor = Pal.Amber;
                    cb.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    cb.Checked = prevAcc.Contains(s);
                    cb.CheckedChanged += OnAnyChange;
                    accPanel.Controls.Add(cb);
                }

                tblHost.Controls.Clear();
                cells.Clear();
                TableLayoutPanel tl = new TableLayoutPanel();
                tl.AutoSize = true;
                tl.ColumnCount = alpha.Count + 1;
                tl.RowCount = states.Count + 1;

                for (int j = 0; j < alpha.Count; j++)
                {
                    Label h = new Label();
                    h.Text = alpha[j];
                    h.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
                    h.ForeColor = Pal.SymColor(j);
                    h.AutoSize = true;
                    h.Anchor = AnchorStyles.None;
                    tl.Controls.Add(h, j + 1, 0);
                }
                for (int i = 0; i < states.Count; i++)
                {
                    Label rl = new Label();
                    rl.Text = states[i];
                    rl.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    rl.ForeColor = Pal.Blue;
                    rl.AutoSize = true;
                    rl.Anchor = AnchorStyles.Right;
                    tl.Controls.Add(rl, 0, i + 1);
                    for (int j = 0; j < alpha.Count; j++)
                    {
                        string key = Dfa.Key(states[i], alpha[j]);
                        ComboBox cb = new ComboBox();
                        cb.DropDownStyle = ComboBoxStyle.DropDownList;
                        cb.Width = 70;
                        foreach (string s in states) cb.Items.Add(s);
                        string cv;
                        if (!mem.TryGetValue(key, out cv) || !states.Contains(cv)) cv = states[0];
                        mem[key] = cv;
                        cb.SelectedIndex = states.IndexOf(cv);
                        cb.SelectedIndexChanged += OnAnyChange;
                        cells[key] = cb;
                        tl.Controls.Add(cb, j + 1, i + 1);
                    }
                }
                tblHost.Controls.Add(tl);
            }
            finally
            {
                loading = false;
            }
        }

        Dfa ReadDfa()
        {
            List<string> states = Parse(tbStates.Text);
            List<string> alpha = Parse(tbAlpha.Text);
            if (Validate(states, alpha).Length > 0 || cbStart.SelectedIndex < 0) return null;
            Dfa d = new Dfa();
            d.States = states;
            d.Alpha = alpha;
            d.Start = cbStart.Text;
            d.Acc = CheckedAccepting();
            foreach (string s in states)
            {
                foreach (string a in alpha)
                {
                    string key = Dfa.Key(s, a);
                    ComboBox c;
                    if (!cells.TryGetValue(key, out c) || c.SelectedIndex < 0) return null;
                    d.T[key] = c.Text;
                    mem[key] = c.Text;
                }
            }
            return d;
        }

        void LoadExample(Example ex)
        {
            loading = true;
            tbStates.Text = ex.States;
            tbAlpha.Text = ex.Alpha;
            tbTest.Text = ex.Test;
            mem.Clear();
            foreach (string tr in ex.Trans)
            {
                string[] p = tr.Split('|');
                mem[Dfa.Key(p[0], p[1])] = p[2];
            }
            loading = false;

            Rebuild(new HashSet<string>(ex.Acc.Split(',')));

            loading = true;
            cbStart.SelectedIndex = Parse(ex.States).IndexOf(ex.Start);
            loading = false;
            Run();
        }

        // ---------------- solve + show ----------------
        void ClearOutputs()
        {
            rtEq.Clear();
            rtSteps.Clear();
            rtProps.Clear();
            rtResult.Clear();
            resultText = "";
            lblVerify.Text = "";
            lblVerdict.Text = "";
            lblTrace.Text = "";
            diagram.Model = null;
            diagram.Info = null;
            diagram.Path = null;
            diagram.Invalidate();
        }

        void Run()
        {
            Dfa d = ReadDfa();
            cur = d;
            curA = null;
            if (d == null)
            {
                ClearOutputs();
                return;
            }
            try
            {
                stateOrder = d.States.OrderByDescending(q => q.Length).ToList();
                alphaList = d.Alpha;
                SolveResult r = Solver.Solve(d);
                Analysis an = Analysis.Compute(d);
                curA = an;
                resultText = Rx.ToStr(r.Result);

                FillEq(r);
                FillSteps(r);
                FillResult();
                FillProps(an, r.Result);

                VerifyResult v = Solver.Verify(d, r.Result);
                if (v.Ok)
                {
                    lblVerify.ForeColor = Color.FromArgb(18, 122, 61);
                    lblVerify.Text = "\u2714  Verified: DFA and regex agree on all " + v.Checked.ToString("N0")
                                     + " strings of length up to " + v.L + ".";
                }
                else
                {
                    lblVerify.ForeColor = Color.Firebrick;
                    lblVerify.Text = "\u2718  Mismatch on string \"" + v.Bad + "\" (please report this).";
                }
                diagram.Model = d;
                diagram.Info = an;
            }
            catch (Exception ex)
            {
                lblVerify.ForeColor = Color.Firebrick;
                lblVerify.Text = "Error: " + ex.Message;
                diagram.Model = null;
                diagram.Info = null;
            }
            UpdateTest();
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
