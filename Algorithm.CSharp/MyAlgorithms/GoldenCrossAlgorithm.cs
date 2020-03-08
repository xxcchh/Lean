using System;
using System.Collections.Generic;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.CSharp.MyAlgorithms
{
    public class GoldenCrossAlgorithm : QCAlgorithm
    {
        public override void Initialize()
        {
            UniverseSettings.Resolution = Resolution.Daily;

            SetStartDate(2017, 1, 1);
            SetEndDate(2018, 12, 31);
            SetCash(10000);

            AddUniverseSelection(new ManualUniverseSelectionModel(QuantConnect.Symbol.Create("AAPL", SecurityType.Equity, Market.USA), QuantConnect.Symbol.Create("GOOGL", SecurityType.Equity, Market.USA)));
            AddAlpha(new GoldenCrossAlphaModel());
            SetPortfolioConstruction(new EqualWeightingPortfolioConstructionModel());
            SetExecution(new ImmediateExecutionModel());
            SetRiskManagement(new MaximumDrawdownPercentPerSecurity(0.05m));
        }

        public override void OnEndOfDay(Symbol symbol)
        {
            Log($"Taking %{Portfolio[symbol].Quantity} units of symbol {symbol}.");
        }

        public class GoldenCrossAlphaModel : AlphaModel
        {
            readonly int d5 = 5;
            readonly int d10 = 10;
            readonly TimeSpan period = TimeSpan.FromDays(1);

            Dictionary<Symbol, SimpleMovingAverage> spreadMeans5Days;
            Dictionary<Symbol, SimpleMovingAverage> spreadMeans10Days;

            public GoldenCrossAlphaModel()
            {
                spreadMeans5Days = new Dictionary<Symbol, SimpleMovingAverage>();
                spreadMeans10Days = new Dictionary<Symbol, SimpleMovingAverage>();
            }

            public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
            {
                // For added securities, we calcualte their 5MA and 10MA using history.
                foreach (var security in changes.AddedSecurities)
                {
                    spreadMeans5Days[security.Symbol] = new SimpleMovingAverage(d5);
                    spreadMeans10Days[security.Symbol] = new SimpleMovingAverage(d10);

                    foreach (var slice in algorithm.History(security.Symbol, d10))
                    {
                        spreadMeans5Days[security.Symbol].Update(slice.Time, slice.Close);
                        spreadMeans10Days[security.Symbol].Update(slice.Time, slice.Close);
                    }
                }

                // For removed securities, we remove them from our list.
                foreach (var security in changes.RemovedSecurities)
                {
                    spreadMeans5Days.Remove(security.Symbol);
                    spreadMeans10Days.Remove(security.Symbol);
                }
            }

            public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
            {
                var insights = new List<Insight>();

                // Update MA for each security.
                foreach (var security in algorithm.ActiveSecurities.Values)
                {
                    spreadMeans5Days[security.Symbol].Update(algorithm.Time, security.Price);
                    spreadMeans10Days[security.Symbol].Update(algorithm.Time, security.Price);

                    var mad5 = spreadMeans5Days[security.Symbol];
                    var mad10 = spreadMeans10Days[security.Symbol];

                    if (mad5 > mad10 && mad5.IsReady && mad10.IsReady)
                    {
                        insights.Add(Insight.Price(security.Symbol, period, InsightDirection.Up));
                    }

                    if (mad5 <= mad10 && mad5.IsReady && mad10.IsReady)
                    {
                        insights.Add(Insight.Price(security.Symbol, period, InsightDirection.Down));
                    }
                }

                return insights;
            }
        }
    }
}
