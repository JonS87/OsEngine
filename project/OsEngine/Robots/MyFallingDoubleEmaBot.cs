using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Market.Servers;
using OsEngine.Market;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
//using static System.Windows.Forms.VisualStyles.VisualStyleElement;
//using OsEngine.Market.Servers.Transaq.TransaqEntity;
using Candle = OsEngine.Entity.Candle;
//using Tinkoff.InvestApi.V1;
//using Google.Protobuf.WellKnownTypes;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

namespace OsEngine.Robots
{
    [Bot("MyFallingDoubleEmaBot")]
    internal class MyFallingDoubleEmaBot : BotPanel
    {
        // Основные параметры
        public StrategyParameterString Regime;
        public StrategyParameterDecimal Volume;
        public StrategyParameterString VolumeType;
        public StrategyParameterInt EmaShortLen;
        public StrategyParameterInt EmaLongLen;
        public StrategyParameterString TradeAssetInPortfolio;
        public StrategyParameterBool UsePriceDropFilter;
        public StrategyParameterDecimal DrawdownPercent;
        public StrategyParameterDecimal PriceDropPercent;
        public StrategyParameterInt LookbackCandles;
        public StrategyParameterDecimal VolumeIncreaseThreshold;
        public StrategyParameterBool UseVolumeConfirmation;

        // Технические параметры
        public StrategyParameterDecimal Slippage;
        public StrategyParameterBool EmaIncline;
        private StrategyParameterTimeOfDay TimeStart;
        private StrategyParameterTimeOfDay TimeEnd;

        // Индикаторы и вкладки
        public BotTabSimple TabToTrade;
        public Aindicator EmaShort;
        public Aindicator EmaLong;
        public Aindicator VolumeIndicator;

        // Текущие значения
        private decimal _maxPrice;
        private decimal _minPrice;
        private decimal _lastEmaShort;
        private decimal _lastEmaLong;
        private decimal _preLastEmaShort;
        private decimal _preLastEmaLong;
        private decimal _lastVolume;
        private decimal _preLastVolume;
        private List<decimal> _priceHistory = new List<decimal>();
        private List<decimal> _volumeHistory = new List<decimal>();
        decimal minLastEmaShort = 1000000000; // для шортов
        decimal maxLastEmaShort = 0;

        public MyFallingDoubleEmaBot(string name, StartProgram startProgram)
            : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            TabToTrade = TabsSimple[0];

            // Подписка на события
            TabToTrade.CandleFinishedEvent += TabToTrade_CandleFinishedEvent;

            // Параметры стратегии
            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            VolumeType = CreateParameter("Volume type", "Deposit percent", new[] { "Contracts", "Contract currency", "Deposit percent" });
            Volume = CreateParameter("Volume", 100, 1.0m, 50, 4);
            EmaShortLen = CreateParameter("EmaShort Len", 15, 5, 63, 3);
            EmaLongLen = CreateParameter("EmaLong Len", 30, 8, 66, 3);
            Slippage = CreateParameter("Slippage %", 5, 0, 20, 1m);
            TradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime");
            DrawdownPercent = CreateParameter("Price drop % to exit", 2m, 0.1m, 10m, 0.1m);
            UsePriceDropFilter = CreateParameter("Use Price Drop Filter", true);
            PriceDropPercent = CreateParameter("Price drop % to enter", 2m, 0.1m, 10m, 0.1m);
            LookbackCandles = CreateParameter("Lookback candles", 10, 5, 50, 1);
            UseVolumeConfirmation = CreateParameter("Use volume confirm", true);
            VolumeIncreaseThreshold = CreateParameter("Volume increase %", 20m, 5m, 100m, 5m);
            EmaIncline = CreateParameter("Ema Incline Regime", true);

            TimeStart = CreateParameterTimeOfDay("Start Trade Time", 10, 32, 0, 0);
            TimeEnd = CreateParameterTimeOfDay("End Trade Time", 18, 25, 0, 0);

            // Создание индикаторов
            EmaShort = IndicatorsFactory.CreateIndicatorByName("Ema", name + "emashort", false);
            EmaShort = (Aindicator)TabToTrade.CreateCandleIndicator(EmaShort, "Prime");
            EmaShort.ParametersDigit[0].Value = EmaShortLen.ValueInt;

            EmaLong = IndicatorsFactory.CreateIndicatorByName("Ema", name + "emalong", false);
            EmaLong = (Aindicator)TabToTrade.CreateCandleIndicator(EmaLong, "Prime");
            EmaLong.ParametersDigit[0].Value = EmaLongLen.ValueInt;

            VolumeIndicator = IndicatorsFactory.CreateIndicatorByName("Volume", name + "Volume", false);
            VolumeIndicator = (Aindicator)TabToTrade.CreateCandleIndicator(VolumeIndicator, "Volume");

            ParametrsChangeByUser += MyFallingDoubleEmaBot_ParametrsChangeByUser;
        }

        private void MyFallingDoubleEmaBot_ParametrsChangeByUser()
        {
            EmaShort.ParametersDigit[0].Value = EmaShortLen.ValueInt;
            EmaShort.Save();
            EmaShort.Reload();

            EmaLong.ParametersDigit[0].Value = EmaLongLen.ValueInt;
            EmaLong.Save();
            EmaLong.Reload();
        }

        private void TabToTrade_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off" || candles.Count < LookbackCandles.ValueInt + 5)
                return;

            if (EmaShortLen.ValueInt >= EmaLongLen.ValueInt)
            {
                return;
            }

            UpdateIndicatorsValues(candles);
            UpdatePriceAndVolumeHistory(candles);

            var openPositions = TabToTrade.PositionsOpenAll;

            if (openPositions == null || openPositions.Count == 0)
            {
                if (Regime.ValueString != "OnlyClosePosition")
                    CheckForEntrySignals(candles);
            }
            else
            {
                CheckForExitSignals(openPositions[0]);
            }
        }

        private void UpdateIndicatorsValues(List<Candle> candles)
        {
            // Обновляет последние значения индикаторов EMA и объема.
            _preLastEmaShort = EmaShort.DataSeries[0].Values[EmaShort.DataSeries[0].Values.Count - 2];
            _preLastEmaLong = EmaLong.DataSeries[0].Values[EmaLong.DataSeries[0].Values.Count - 2];
            _lastEmaShort = EmaShort.DataSeries[0].Last;
            _lastEmaLong = EmaLong.DataSeries[0].Last;

            _preLastVolume = VolumeIndicator.DataSeries[0].Values[VolumeIndicator.DataSeries[0].Values.Count - 2];
            _lastVolume = VolumeIndicator.DataSeries[0].Last;
        }

        private void UpdatePriceAndVolumeHistory(List<Candle> candles)
        {
            // Сохраняет историю цен и объемов за последние LookbackCandles свечей
            _priceHistory.Clear();
            _volumeHistory.Clear();

            int startIndex = Math.Max(0, candles.Count - LookbackCandles.ValueInt);

            for (int i = startIndex; i < candles.Count; i++)
            {
                _priceHistory.Add(candles[i].Close);
                _volumeHistory.Add(VolumeIndicator.DataSeries[0].Values[i]);
            }

            _maxPrice = GetMaxPrice();
            _minPrice = GetMinPrice();

            if (candles[candles.Count - 1].Close > maxLastEmaShort)
            {
                maxLastEmaShort = candles[candles.Count - 1].Close;
            }

            if (candles[candles.Count - 1].Close < minLastEmaShort && candles[candles.Count - 1].Close > 0)
            {
                minLastEmaShort = candles[candles.Count - 1].Close;
            }
        }

        private decimal GetMaxPrice()
        {
            // Возвращает максимальную цену из истории (_priceHistory)
            decimal max = decimal.MinValue;
            foreach (var price in _priceHistory)
            {
                if (price > max) max = price;
            }
            return max;
        }

        private decimal GetMinPrice()
        {
            // Возвращает минимальную цену из истории (_priceHistory)
            decimal min = decimal.MaxValue;
            foreach (var price in _priceHistory)
            {
                if (price < min) min = price;
            }
            return min;
        }

        private bool IsPriceDroppedFromHigh(decimal currentPrice)
        {
            // Проверяет, упала ли цена с максимума на заданный процент (PriceDropPercent).
            decimal dropPercent = (_maxPrice - currentPrice) / _maxPrice * 100;
            return dropPercent >= PriceDropPercent.ValueDecimal;
        }

        private bool IsPriceRisedFromLow(decimal currentPrice)
        {
            // Проверяет, выросла ли с минимума на заданный процент (PriceDropPercent).
            decimal risePercent = (currentPrice - _minPrice) / _minPrice * 100;
            return risePercent >= PriceDropPercent.ValueDecimal;
        }

        private bool IsVolumeIncreasing()
        {
            // Проверяет, превышает ли последний объем средний объем за предыдущие свечи на порог VolumeIncreaseThreshold
            if (_volumeHistory.Count < 2) return false;

            decimal avgVolume = 0;
            for (int i = 0; i < _volumeHistory.Count - 1; i++)
            {
                avgVolume += _volumeHistory[i];
            }
            avgVolume /= (_volumeHistory.Count - 1);

            decimal lastVol = _volumeHistory[_volumeHistory.Count - 1];
            decimal increasePercent = (lastVol - avgVolume) / avgVolume * 100;

            return increasePercent >= VolumeIncreaseThreshold.ValueDecimal;
        }

        private void CheckForEntrySignals(List<Candle> candles)
        {
            if (TimeStart.Value > TabToTrade.TimeServerCurrent ||
                TimeEnd.Value < TabToTrade.TimeServerCurrent)
            {
                return;
            }

            if (_preLastEmaLong <= 0)
            {
                return;
            }

            // Проверяет условия для открытия позиций
            decimal currentPrice = candles[candles.Count - 1].Close;

            // Логика для лонга
            if (Regime.ValueString != "OnlyShort" &&
                (!UsePriceDropFilter.ValueBool || IsPriceDroppedFromHigh(currentPrice)) &&
                _lastEmaShort > _lastEmaLong &&
                _preLastEmaShort <= _preLastEmaLong)
            {
                bool volumeConfirm = !UseVolumeConfirmation.ValueBool || IsVolumeIncreasing();
                bool inclineConfirm = !EmaIncline.ValueBool || _lastEmaShort > _preLastEmaShort;

                if (volumeConfirm && inclineConfirm)
                {
                    decimal quantity = GetVolume();
                    TabToTrade.BuyAtLimit(quantity, currentPrice + currentPrice * (Slippage.ValueDecimal / 100));
                }
            }

            // Логика для шорта
            if (Regime.ValueString != "OnlyLong" &&
                (!UsePriceDropFilter.ValueBool || IsPriceRisedFromLow(currentPrice)) &&
                _lastEmaShort < _lastEmaLong &&
                _preLastEmaShort >= _preLastEmaLong)
            {
                bool volumeConfirm = !UseVolumeConfirmation.ValueBool || IsVolumeIncreasing();
                bool inclineConfirm = !EmaIncline.ValueBool || _lastEmaShort < _preLastEmaShort;

                if (volumeConfirm && inclineConfirm)
                {
                    decimal quantity = GetVolume();
                    TabToTrade.SellAtLimit(quantity, currentPrice - currentPrice * (Slippage.ValueDecimal / 100));
                }
            }
        }

        private void CheckForExitSignals(Position position)
        {
            // Определяет условия для закрытия позиции
            if (position.State != PositionStateType.Open 
                ||
                (position.CloseOrders != null 
                 && position.CloseOrders.Count > 0))
                return;

            decimal currentPrice = TabToTrade.PriceBestBid;

            if (position.Direction == Side.Buy)
            {
                if (// _lastEmaShort < _lastEmaLong ||
                    currentPrice <= maxLastEmaShort * (1 - DrawdownPercent.ValueDecimal / 100)) // currentPrice <= position.EntryPrice * (1 - DrawdownPercent.ValueDecimal))
                {
                    TabToTrade.CloseAtLimit(position, currentPrice - currentPrice * (Slippage.ValueDecimal / 100), position.OpenVolume);
                }
            }
            else if (position.Direction == Side.Sell)
            {
                if (// _lastEmaShort > _lastEmaLong ||
                    currentPrice >= minLastEmaShort * (1 + DrawdownPercent.ValueDecimal / 100)) // currentPrice >= position.EntryPrice * (1 + DrawdownPercent.ValueDecimal))
                {
                    TabToTrade.CloseAtLimit(position, currentPrice + currentPrice * (Slippage.ValueDecimal / 100), position.OpenVolume);
                }
            }
        }

        private decimal GetVolume()
        {
            // Рассчитывает объем позиции в зависимости от выбранного типа
            decimal volume = 0;

            if (VolumeType.ValueString == "Contracts")
            {
                volume = Volume.ValueDecimal;
            }
            else if (VolumeType.ValueString == "Contract currency")
            {
                decimal contractPrice = TabToTrade.PriceBestAsk;
                volume = Volume.ValueDecimal / contractPrice;

                if (StartProgram == StartProgram.IsOsTrader)
                {
                    IServerPermission serverPermission = ServerMaster.GetServerPermission(TabToTrade.Connector.ServerType);

                    if (serverPermission != null &&
                        serverPermission.IsUseLotToCalculateProfit &&
                    TabToTrade.Securiti.Lot != 0 &&
                        TabToTrade.Securiti.Lot > 1)
                    {
                        volume = Volume.ValueDecimal / (contractPrice * TabToTrade.Securiti.Lot);
                    }

                    volume = Math.Round(volume, TabToTrade.Securiti.DecimalsVolume);
                }
                else // Tester or Optimizer
                {
                    volume = Math.Round(volume, 6);
                }
            }
            else if (VolumeType.ValueString == "Deposit percent")
            {
                Portfolio myPortfolio = TabToTrade.Portfolio;

                if (myPortfolio == null)
                {
                    return 0;
                }

                decimal portfolioPrimeAsset = 0;

                if (TradeAssetInPortfolio.ValueString == "Prime")
                {
                    portfolioPrimeAsset = myPortfolio.ValueCurrent;
                }
                else
                {
                    List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();

                    if (positionOnBoard == null)
                    {
                        return 0;
                    }

                    for (int i = 0; i < positionOnBoard.Count; i++)
                    {
                        if (positionOnBoard[i].SecurityNameCode == TradeAssetInPortfolio.ValueString)
                        {
                            portfolioPrimeAsset = positionOnBoard[i].ValueCurrent;
                            break;
                        }
                    }
                }

                if (portfolioPrimeAsset == 0)
                {
                    SendNewLogMessage("Can`t found portfolio " + TradeAssetInPortfolio.ValueString, Logging.LogMessageType.Error);
                    return 0;
                }

                decimal moneyOnPosition = portfolioPrimeAsset * (Volume.ValueDecimal / 100);

                decimal qty = moneyOnPosition / TabToTrade.PriceBestAsk / TabToTrade.Securiti.Lot;

                if (TabToTrade.StartProgram == StartProgram.IsOsTrader)
                {
                    qty = Math.Round(qty, TabToTrade.Securiti.DecimalsVolume);
                }
                else
                {
                    qty = Math.Round(qty, 7);
                }

                return qty;
            }

            return volume;
        }

        public override string GetNameStrategyType()
        {
            return "MyFallingDoubleEmaBot";
        }

        public override void ShowIndividualSettingsDialog()
        {
            //throw new NotImplementedException();
        }
    }
}