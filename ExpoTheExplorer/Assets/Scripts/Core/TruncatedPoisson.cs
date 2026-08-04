using System;

namespace ExpoTheExplorer.Core
{
    // Poisson(lambda) pmf truncated to k = 0..n and renormalized — the
    // un-truncated Poisson has no upper bound, but every caller has a hard
    // ceiling (available modifications, un-leaked upcoming tickets, etc.).
    // Uses the unnormalized term ratio lambda^k/k! directly, built via the
    // recurrence term(k) = term(k-1) * lambda / k — the e^-lambda factor of
    // the true Poisson pmf cancels out once terms are divided by their sum,
    // so it's never computed. term(0) is always exactly 1, so the sum is
    // always >= 1 — no divide-by-zero risk.
    public static class TruncatedPoisson
    {
        public static int Sample(int n, float lambda, Random random)
        {
            if (n == 0) return 0;

            var terms = Terms(n, lambda);

            var total = 0d;
            foreach (var term in terms) total += term;

            var roll = random.NextDouble() * total;
            var cumulative = 0d;
            for (var k = 0; k <= n; k++)
            {
                cumulative += terms[k];
                if (roll < cumulative) return k;
            }

            return n;
        }

        public static double[] Probabilities(int n, float lambda)
        {
            var terms = Terms(n, lambda);

            var total = 0d;
            foreach (var term in terms) total += term;

            for (var k = 0; k <= n; k++) terms[k] /= total;
            return terms;
        }

        private static double[] Terms(int n, float lambda)
        {
            var terms = new double[n + 1];
            terms[0] = 1d;
            for (var k = 1; k <= n; k++)
            {
                terms[k] = terms[k - 1] * lambda / k;
            }

            return terms;
        }
    }
}
