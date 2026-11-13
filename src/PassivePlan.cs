using System;
using System.Collections.Generic;
using System.Linq;

namespace Poe2ItemCompanion
{
    // One step towards the guide tree: the path (in allocation order) to the next important node.
    public class PassiveTarget
    {
        public TreeNode Target { get; set; }            // notable/keystone/socket the path leads to; null = leftover small passives
        public List<TreeNode> Path { get; set; }        // nodes to allocate in order, the target last
        public PassiveTarget() { Path = new List<TreeNode>(); }
    }

    public class PassivePlanResult
    {
        public string Notice { get; set; }
        public List<TreeNode> MasteryChanges { get; set; }
        public bool CurrentLoaded { get; set; }
        public int GuidePoints { get; set; }
        public int CurrentPoints { get; set; }
        public int GuideAscendancyPoints { get; set; }
        public int CurrentAscendancyPoints { get; set; }
        public List<PassiveTarget> Targets { get; set; }
        public List<PassiveTarget> AscendancyTargets { get; set; }
        public List<TreeNode> NotInGuide { get; set; }   // allocated on the Current character but not in this guide stage
        public List<TreeNode> Unreachable { get; set; }  // guide nodes with no guide path from the current tree
        public int MissingCount { get { return Targets.Sum(t => t.Path.Count) + Unreachable.Count(n => !n.Ascendancy); } }
        public int MissingAscendancyCount { get { return AscendancyTargets.Sum(t => t.Path.Count) + Unreachable.Count(n => n.Ascendancy); } }

        public PassivePlanResult()
        {
            MasteryChanges = new List<TreeNode>();
            Targets = new List<PassiveTarget>();
            AscendancyTargets = new List<PassiveTarget>();
            NotInGuide = new List<TreeNode>();
            Unreachable = new List<TreeNode>();
        }
    }

    // Suggests what to allocate next: from the Current character's tree, the shortest path over guide-stage nodes
    // to the nearest guide notable/keystone/socket, repeated. The guide has no levelling order; this is a suggestion.
    public static class PassivePlan
    {
        public static bool IsStart(TreeNode n)
        {
            return n.Type == "ClassStart" || n.Type == "AscendClassStart";
        }

        public static bool IsImportant(TreeNode n)
        {
            return n.Type == "Keystone" || n.Type == "Notable" || n.Type == "Socket";
        }

        private static int Rank(TreeNode n)
        {
            return n.Type == "Keystone" ? 0 : n.Type == "Notable" ? 1 : n.Type == "Socket" ? 2 : 3;
        }

        public static PassivePlanResult Plan(BuildDetails guide, BuildDetails current)
        {
            var result = new PassivePlanResult { CurrentLoaded = current != null && current.Tree != null };
            if (guide == null || guide.Tree == null) return result;
            if (current != null && ((guide.Game ?? "") != (current.Game ?? "")
                || (guide.TreeVersion ?? "") != (current.TreeVersion ?? "")))
            {
                result.Notice = "Current and Guide use different games or tree versions. Select matching trees before using allocation advice.";
                result.CurrentLoaded = false;
                return result;
            }
            var nodes = new Dictionary<int, TreeNode>();
            foreach (var n in guide.Tree.Nodes) nodes[n.Id] = n;
            var incompatible = new HashSet<int>();
            if (current != null)
                foreach (var n in current.Tree.Nodes)
                {
                    TreeNode target;
                    if (n.Id >= 65536 && nodes.TryGetValue(n.Id, out target)
                        && (n.GraphContext != target.GraphContext || n.Name != target.Name
                            || Math.Abs(n.X - target.X) > 0.1 || Math.Abs(n.Y - target.Y) > 0.1))
                        incompatible.Add(n.Id);
                }
            bool clusterContextsDiffer = current != null && !new HashSet<string>(guide.Tree.Nodes.Where(n => n.Id >= 65536)
                .Select(n => n.GraphContext ?? "unknown")).SetEquals(current.Tree.Nodes.Where(n => n.Id >= 65536).Select(n => n.GraphContext ?? "unknown"));
            if (incompatible.Count > 0 || clusterContextsDiffer)
            {
                result.Notice = "Current and Guide cluster-jewel graphs differ. Allocation/refund advice is unavailable until their cluster setups match.";
                result.CurrentLoaded = false;
                return result;
            }
            var adj = new Dictionary<int, List<int>>();
            foreach (var e in guide.Tree.Edges)
            {
                if (e == null || e.Count < 2) continue;
                TreeNode first, second;
                if (!nodes.TryGetValue(e[0], out first) || !nodes.TryGetValue(e[1], out second)
                    || first.Type == "Mastery" || second.Type == "Mastery") continue;
                AddEdge(adj, e[0], e[1]);
                AddEdge(adj, e[1], e[0]);
            }

            var guideAlloc = new HashSet<int>(guide.Tree.Nodes.Where(n => n.Alloc).Select(n => n.Id));
            var have = new HashSet<int>();
            if (result.CurrentLoaded)
            {
                foreach (var n in current.Tree.Nodes.Where(x => x.Alloc))
                {
                    if (IsStart(n)) continue;
                    TreeNode target;
                    if (n.Type == "Mastery")
                    {
                        if (nodes.TryGetValue(n.Id, out target) && target.Alloc && target.MasteryEffect != n.MasteryEffect)
                            result.MasteryChanges.Add(target);
                        continue;
                    }
                    if (n.Ascendancy) result.CurrentAscendancyPoints++; else result.CurrentPoints++;
                    if (nodes.ContainsKey(n.Id)) have.Add(n.Id);
                    if (!guideAlloc.Contains(n.Id)) result.NotInGuide.Add(nodes.ContainsKey(n.Id) ? nodes[n.Id] : n);
                }
            }
            // The class (and ascendancy) start nodes are always "allocated".
            foreach (var n in guide.Tree.Nodes.Where(x => x.Alloc && IsStart(x))) have.Add(n.Id);
            if (result.CurrentLoaded)
                foreach (var n in current.Tree.Nodes.Where(x => x.Alloc && IsStart(x) && nodes.ContainsKey(x.Id))) have.Add(n.Id);

            foreach (var n in guide.Tree.Nodes.Where(x => x.Alloc && !IsStart(x) && x.Type != "Mastery"))
            {
                if (n.Ascendancy) result.GuideAscendancyPoints++; else result.GuidePoints++;
            }

            var remaining = new HashSet<int>(guideAlloc.Where(id => !have.Contains(id) && !IsStart(nodes[id]) && nodes[id].Type != "Mastery"));
            foreach (var n in guide.Tree.Nodes.Where(n => n.Alloc && n.Type == "Mastery"))
                if (!result.MasteryChanges.Any(m => m.Id == n.Id)
                    && (current == null || !current.Tree.Nodes.Any(c => c.Alloc && c.Id == n.Id && c.MasteryEffect == n.MasteryEffect)))
                    result.MasteryChanges.Add(n);
            result.Targets = Walk(nodes, adj, have, new HashSet<int>(remaining.Where(id => !nodes[id].Ascendancy)), result.Unreachable);
            result.AscendancyTargets = Walk(nodes, adj, have, new HashSet<int>(remaining.Where(id => nodes[id].Ascendancy)), result.Unreachable);
            result.NotInGuide = result.NotInGuide.OrderBy(n => Rank(n)).ThenBy(n => n.Name).ToList();
            return result;
        }

        private static void AddEdge(Dictionary<int, List<int>> adj, int a, int b)
        {
            List<int> list;
            if (!adj.TryGetValue(a, out list)) adj[a] = list = new List<int>();
            list.Add(b);
        }

        private static List<PassiveTarget> Walk(Dictionary<int, TreeNode> nodes, Dictionary<int, List<int>> adj, HashSet<int> have,
            HashSet<int> remaining, List<TreeNode> unreachable)
        {
            var targets = new List<PassiveTarget>();
            while (remaining.Count > 0)
            {
                // Multi-source BFS from everything already allocated, only through nodes the guide allocates.
                var parent = new Dictionary<int, int>();
                var dist = new Dictionary<int, int>();
                var order = new List<int>();
                var queue = new Queue<int>();
                foreach (var id in have.OrderBy(x => x))
                {
                    List<int> next;
                    if (!adj.TryGetValue(id, out next)) continue;
                    foreach (var nb in next.OrderBy(x => x))
                        if (remaining.Contains(nb) && !dist.ContainsKey(nb)) { dist[nb] = 1; parent[nb] = -1; queue.Enqueue(nb); order.Add(nb); }
                }
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    List<int> next;
                    if (!adj.TryGetValue(id, out next)) continue;
                    foreach (var nb in next.OrderBy(x => x))
                        if (remaining.Contains(nb) && !dist.ContainsKey(nb)) { dist[nb] = dist[id] + 1; parent[nb] = id; queue.Enqueue(nb); order.Add(nb); }
                }
                if (order.Count == 0)
                {
                    unreachable.AddRange(remaining.Select(id => nodes[id]).OrderBy(n => Rank(n)).ThenBy(n => n.Name));
                    break;
                }
                var important = order.Where(id => IsImportant(nodes[id])).OrderBy(id => dist[id]).ThenBy(id => Rank(nodes[id])).ThenBy(id => order.IndexOf(id)).ToList();
                var step = new PassiveTarget();
                if (important.Count > 0)
                {
                    int target = important[0];
                    step.Target = nodes[target];
                    var path = new List<int>();
                    for (int id = target; id != -1; id = parent[id]) path.Add(id);
                    path.Reverse();
                    step.Path = path.Select(id => nodes[id]).ToList();
                }
                else
                {
                    step.Path = order.Select(id => nodes[id]).ToList();
                }
                foreach (var n in step.Path) { have.Add(n.Id); remaining.Remove(n.Id); }
                targets.Add(step);
            }
            return targets;
        }

        // Short description of a node for the overlay: first stat line, or the node type.
        public static string Describe(TreeNode n)
        {
            if (n.Stats != null && n.Stats.Count > 0) return string.Join("; ", n.Stats.Take(2).ToArray());
            return n.Type;
        }
    }
}
