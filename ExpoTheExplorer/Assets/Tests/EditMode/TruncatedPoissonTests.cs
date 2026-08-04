using System;
using ExpoTheExplorer.Core;
using NUnit.Framework;

namespace ExpoTheExplorer.Tests.EditMode
{
    public class TruncatedPoissonTests
    {
        private const float ExtremeLambda = 1_000_000f;

        [Test]
        public void Sample_LambdaZero_AlwaysReturnsZero()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                Assert.AreEqual(0, TruncatedPoisson.Sample(4, 0f, new Random(seed)));
            }
        }

        [Test]
        public void Sample_NZero_AlwaysReturnsZero()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                Assert.AreEqual(0, TruncatedPoisson.Sample(0, 2f, new Random(seed)));
            }
        }

        [Test]
        public void Sample_ExtremeLambda_AlwaysReturnsN()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                Assert.AreEqual(4, TruncatedPoisson.Sample(4, ExtremeLambda, new Random(seed)));
            }
        }

        [Test]
        public void Sample_ModerateLambda_ClustersNearLambda_NotMonotonicallyDecreasing()
        {
            const int n = 4;
            const float lambda = 2f; // Poisson(2) truncated to 0..4: P(0)~14%, P(2)~29%
            const int sampleSize = 4000;

            var countAtZero = 0;
            var countAtTwo = 0;
            for (var seed = 0; seed < sampleSize; seed++)
            {
                var sample = TruncatedPoisson.Sample(n, lambda, new Random(seed));
                if (sample == 0) countAtZero++;
                if (sample == 2) countAtTwo++;
            }

            Assert.Greater(countAtTwo, countAtZero * 1.3);
        }

        [Test]
        public void Probabilities_SumToOne()
        {
            var probabilities = TruncatedPoisson.Probabilities(4, 2f);

            var total = 0d;
            foreach (var p in probabilities) total += p;

            Assert.AreEqual(1d, total, 1e-9);
        }

        [Test]
        public void Probabilities_LambdaZero_AllMassAtZero()
        {
            var probabilities = TruncatedPoisson.Probabilities(4, 0f);

            Assert.AreEqual(1d, probabilities[0], 1e-9);
            for (var k = 1; k <= 4; k++)
            {
                Assert.AreEqual(0d, probabilities[k], 1e-9);
            }
        }
    }
}
