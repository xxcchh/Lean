﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Logging;

namespace QuantConnect.ToolBox.CoinApi
{
    /// <summary>
    /// Provides the mapping between Lean symbols and CoinAPI symbols.
    /// </summary>
    /// <remarks>For now we only support mapping for CoinbasePro (GDAX) and Bitfinex</remarks>
    public class CoinApiSymbolMapper : ISymbolMapper
    {
        private const string RestUrl = "https://rest.coinapi.io";
        private readonly string _apiKey = Config.Get("coinapi-api-key");
        private readonly bool _useLocalSymbolList = Config.GetBool("coinapi-use-local-symbol-list", false);
        private readonly FileInfo _coinApiSymbolsListFile = new FileInfo("CoinApiSymbols.json");
        // LEAN market <-> CoinAPI exchange id maps
        private static readonly Dictionary<string, string> MapMarketsToExchangeIds = new Dictionary<string, string>
        {
            { Market.GDAX, "COINBASE" },
            { Market.Bitfinex, "BITFINEX" }
        };
        private static readonly Dictionary<string, string> MapExchangeIdsToMarkets =
            MapMarketsToExchangeIds.ToDictionary(x => x.Value, x => x.Key);

        private static readonly Dictionary<string, Dictionary<string, string>> CoinApiToLeanCurrencyMappings =
            new Dictionary<string, Dictionary<string, string>>
            {
                {
                    Market.Bitfinex,
                    new Dictionary<string, string>
                    {
                        { "ANIO", "NIO" },
                        { "BCHSV", "BSV" },
                        { "DASH", "DSH" },
                        { "IOTA", "IOT" },
                        { "MANA", "MNA" },
                        { "PKGO", "GOT" },
                        { "QTUM", "QTM" },
                        { "USDT", "UST" },
                        { "YOYOW", "YYW" }
                    }
                }
            };

        // map LEAN symbols to CoinAPI symbol ids
        private Dictionary<Symbol, string> _symbolMap = new Dictionary<Symbol, string>();


        /// <summary>
        /// Creates a new instance of the <see cref="CoinApiSymbolMapper"/> class
        /// </summary>
        public CoinApiSymbolMapper()
        {
            LoadSymbolMap(MapMarketsToExchangeIds.Values.ToArray());
        }

        /// <summary>
        /// Converts a Lean symbol instance to a CoinAPI symbol id
        /// </summary>
        /// <param name="symbol">A Lean symbol instance</param>
        /// <returns>The CoinAPI symbol id</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            string symbolId;
            if (!_symbolMap.TryGetValue(symbol, out symbolId))
            {
                throw new Exception($"CoinApiSymbolMapper.GetBrokerageSymbol(): Symbol not found: {symbol}");
            }

            return symbolId;
        }

        /// <summary>
        /// Converts a CoinAPI symbol id to a Lean symbol instance
        /// </summary>
        /// <param name="brokerageSymbol">The CoinAPI symbol id</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">Expiration date of the security (if applicable)</param>
        /// <param name="strike">The strike of the security (if applicable)</param>
        /// <param name="optionRight">The option right of the security (if applicable)</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market,
            DateTime expirationDate = new DateTime(), decimal strike = 0, OptionRight optionRight = OptionRight.Call)
        {
            var parts = brokerageSymbol.Split('_');
            if (parts.Length != 4 || parts[1] != "SPOT")
            {
                throw new Exception($"CoinApiSymbolMapper.GetLeanSymbol(): Unsupported SymbolId: {brokerageSymbol}");
            }

            string symbolMarket;
            if (!MapExchangeIdsToMarkets.TryGetValue(parts[0], out symbolMarket))
            {
                throw new Exception($"CoinApiSymbolMapper.GetLeanSymbol(): Unsupported ExchangeId: {parts[0]}");
            }

            var baseCurrency = ConvertCoinApiCurrencyToLeanCurrency(parts[2], symbolMarket);
            var quoteCurrency = ConvertCoinApiCurrencyToLeanCurrency(parts[3], symbolMarket);

            var ticker = baseCurrency + quoteCurrency;

            return Symbol.Create(ticker, SecurityType.Crypto, symbolMarket);
        }

        /// <summary>
        /// Returns the CoinAPI exchange id for the given market
        /// </summary>
        /// <param name="market">The Lean market</param>
        /// <returns>The CoinAPI exchange id</returns>
        public string GetExchangeId(string market)
        {
            string exchangeId;
            MapMarketsToExchangeIds.TryGetValue(market, out exchangeId);

            return exchangeId;
        }

        private void LoadSymbolMap(string[] exchangeIds)
        {
            var list = string.Join(",", exchangeIds);
            var json = string.Empty;

            if (_useLocalSymbolList && _coinApiSymbolsListFile.Exists && _coinApiSymbolsListFile.CreationTimeUtc.Date < DateTime.UtcNow.Date.AddDays(-1))
            {
                json = File.ReadAllText(_coinApiSymbolsListFile.FullName);
            }
            else
            {
                using (var wc = new WebClient())
                {
                    var url = $"{RestUrl}/v1/symbols?filter_symbol_id={list}&apiKey={_apiKey}";
                    json = wc.DownloadString(url);
                }

                if (_useLocalSymbolList) File.WriteAllText(_coinApiSymbolsListFile.FullName, json);
            }

            var result = JsonConvert.DeserializeObject<List<CoinApiSymbol>>(json);

            _symbolMap = result
                .Where(x => x.SymbolType == "SPOT")
                .ToDictionary(
                    x =>
                    {
                        var market = MapExchangeIdsToMarkets[x.ExchangeId];
                        return Symbol.Create(
                            ConvertCoinApiCurrencyToLeanCurrency(x.AssetIdBase, market) +
                            ConvertCoinApiCurrencyToLeanCurrency(x.AssetIdQuote, market),
                            SecurityType.Crypto,
                            market);
                    },
                    x => x.SymbolId);
        }

        private static string ConvertCoinApiCurrencyToLeanCurrency(string currency, string market)
        {
            Dictionary<string, string> mappings;
            if (CoinApiToLeanCurrencyMappings.TryGetValue(market, out mappings))
            {
                string mappedCurrency;
                if (mappings.TryGetValue(currency, out mappedCurrency))
                {
                    currency = mappedCurrency;
                }
            }

            return currency;
        }
    }
}
