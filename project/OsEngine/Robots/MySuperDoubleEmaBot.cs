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
    [Bot("MySuperDoubleEmaBot")]
    internal class MySuperDoubleEmaBot : BotPanel
    {
        public StrategyParameterString Regime;
        public StrategyParameterDecimal Volume;
        public StrategyParameterString VolumeType;
        public StrategyParameterInt EmaShortLen;
        public StrategyParameterInt EmaLongLen;
        public StrategyParameterString TradeAssetInPortfolio;
        public StrategyParameterDecimal drawndownPercent;
        public BotTabSimple TabToTrade;
        public Aindicator EmaShort;
        public Aindicator EmaLong;
        public StrategyParameterDecimal Slippage;
        public List<Position> openPoces;
        private StrategyParameterTimeOfDay TimeStart;
        private StrategyParameterTimeOfDay TimeEnd;
        public StrategyParameterBool EmaIncline;

        //decimal prePreLastEmaShort = 0;
        //decimal prePreLastEmaLong = 0;
        decimal moneyOnPosition = 0;
        decimal minLastEmaShort = 1000000000; // для шортов
        decimal maxLastEmaShort = 0;
        decimal preLastEmaShort = 0;
        decimal preLastEmaLong = 0;
        decimal prePreLastEmaShort = 0;
        decimal prePreLastEmaLong = 0;
        decimal lastEmaShort = 0;
        decimal lastEmaLong = 0;
        decimal drawndownPercentDec;
        decimal lastCandleClose;
        decimal lastCandleOpen;

        public StrategyParameterDecimal MinVolatility;
        public StrategyParameterDecimal MaxVolatility;
        public Aindicator Atr;

        public MySuperDoubleEmaBot(string name, StartProgram startProgram)
            : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            TabToTrade = TabsSimple[0];

            EmaShort = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "emashort", canDelete: false);
            EmaShort = (Aindicator)TabToTrade.CreateCandleIndicator(EmaShort, "Prime");

            EmaLong = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "emalong", canDelete: false);
            EmaLong = (Aindicator)TabToTrade.CreateCandleIndicator(EmaLong, "Prime");

            //TabToTrade.CandleUpdateEvent += TabToTrade_CandleUpdateEvent;

            TabToTrade.CandleFinishedEvent += TabToTrade_CandleFinishedEvent;



            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            VolumeType = CreateParameter("Volume type", "Deposit percent", new[] { "Contracts", "Contract currency", "Deposit percent" });
            Volume = CreateParameter("Volume", 100, 1.0m, 50, 4);
            EmaShortLen = CreateParameter("EmaShort Len", 15, 5, 63, 3);
            EmaLongLen = CreateParameter("EmaLong Len", 30, 8, 66, 3);
            Slippage = CreateParameter("Slippage %", 5, 0, 20, 1m);
            TradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime");
            drawndownPercent = CreateParameter("drawndown percent from lastEmaShort", 0.002m, 0.001m, 0.1m, 0.001m);
            EmaIncline = CreateParameter("Ema Incline Regime", true);

            MinVolatility = CreateParameter("Min ATR", 0.5m, 0.1m, 5m, 0.1m);
            MaxVolatility = CreateParameter("Max ATR", 5m, 1m, 20m, 0.5m);
            Atr = IndicatorsFactory.CreateIndicatorByName("ATR", name + "ATR", false);
            Atr = (Aindicator)TabToTrade.CreateCandleIndicator(Atr, "Auxiliary");

            TimeStart = CreateParameterTimeOfDay("Start Trade Time", 10, 32, 0, 0);
            TimeEnd = CreateParameterTimeOfDay("End Trade Time", 18, 25, 0, 0);

            EmaShort.ParametersDigit[0].Value = EmaShortLen.ValueInt;
            EmaShort.Save();
            EmaLong.ParametersDigit[0].Value = EmaLongLen.ValueInt;
            EmaLong.Save();

            drawndownPercentDec = drawndownPercent.ValueDecimal;

            ParametrsChangeByUser += MySuperDoubleEmaBot_ParametrsChangeByUser;
        }

        private void TabToTrade_CandleUpdateEvent(List<Candle> candles)
        {
            lastCandleOpen = candles[candles.Count - 1].Open;
        }

        private void MySuperDoubleEmaBot_ParametrsChangeByUser()
        {
            EmaShort.ParametersDigit[0].Value = EmaShortLen.ValueInt;
            EmaShort.Save();
            EmaShort.Reload();
            EmaLong.ParametersDigit[0].Value = EmaLongLen.ValueInt;
            EmaLong.Save();
            EmaLong.Reload();
            drawndownPercentDec = drawndownPercent.ValueDecimal;
        }

        private decimal GetVolume()
        {
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

                decimal contractPrice = TabToTrade.PriceBestAsk + TabToTrade.Securiti.PriceStep;
                decimal moneyAvailable = portfolioPrimeAsset * 0.95m; // 5% резерв
                decimal moneyOnPosition = moneyAvailable * (Volume.ValueDecimal / 100);
                decimal qty = moneyOnPosition / (contractPrice * TabToTrade.Securiti.Lot);

                // Проверка минимального лота
                qty = Math.Max(qty, TabToTrade.Securiti.Lot);
                qty = Math.Round(qty / TabToTrade.Securiti.Lot) * TabToTrade.Securiti.Lot;

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

        private void TabToTrade_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            if (EmaShortLen.ValueInt >= EmaLongLen.ValueInt)
            {
                return;
            }

            if (candles.Count < 10)
            {
                return;
            }

            lastCandleClose = candles[candles.Count - 1].Close;

            prePreLastEmaShort = EmaShort.DataSeries[0].Values[EmaShort.DataSeries[0].Values.Count - 3];
            prePreLastEmaLong = EmaLong.DataSeries[0].Values[EmaLong.DataSeries[0].Values.Count - 3];
            preLastEmaShort = EmaShort.DataSeries[0].Values[EmaShort.DataSeries[0].Values.Count - 2];
            preLastEmaLong = EmaLong.DataSeries[0].Values[EmaLong.DataSeries[0].Values.Count - 2];

            lastEmaShort = EmaShort.DataSeries[0].Last;
            lastEmaLong = EmaLong.DataSeries[0].Last;

            if (lastEmaShort > maxLastEmaShort)
            {
                maxLastEmaShort = lastEmaShort;
            }

            if (lastEmaShort < minLastEmaShort && lastEmaShort > 0)
            {
                minLastEmaShort = lastEmaShort;
            }

            Portfolio myPortfolio = TabToTrade.Portfolio;

            openPoces = TabToTrade.PositionsOpenAll;

            if (openPoces == null || openPoces.Count == 0)
            {
                if (Regime.ValueString == "OnlyClosePosition")
                {
                    return;
                }
                LogicOpenPosition(candles, myPortfolio);
            }
            else
            {
                LogicClosePosition(openPoces[0]);
            }
        }


        private void LogicOpenPosition(List<Candle> candles, Portfolio myPortfolio)
        {
            decimal checker = 0;
            if (TimeStart.Value > TabToTrade.TimeServerCurrent ||
                TimeEnd.Value < TabToTrade.TimeServerCurrent)
            {
                return;
            }

            if (preLastEmaLong <= 0 ||
                prePreLastEmaLong <= 0)
            {
                return;
            }

            decimal atrValue = Atr.DataSeries[0].Last;
            if (atrValue < MinVolatility.ValueDecimal || atrValue > MaxVolatility.ValueDecimal)
            {
                return; // Пропускаем торговлю при несоответствующей волатильности
            }

            if (lastEmaShort > lastEmaLong
                && (preLastEmaShort <= preLastEmaLong
                    || prePreLastEmaShort <= prePreLastEmaLong)
                && Regime.ValueString != "OnlyShort")
            {
                if (EmaIncline.ValueBool)
                {
                    if (preLastEmaShort >= lastEmaShort)
                    {
                        return;
                    }
                }

                decimal quantity = GetVolume();

                TabToTrade.BuyAtLimit(quantity, lastCandleClose + lastCandleClose * (Slippage.ValueDecimal / 100));
                //TabToTrade.BuyAtMarket(quantity);
                maxLastEmaShort = 0;
            }

            if (lastEmaShort < lastEmaLong
                && (preLastEmaShort >= preLastEmaLong
                    || prePreLastEmaShort >= prePreLastEmaLong)
                && Regime.ValueString != "OnlyLong") // Вход в шорт
            {

                if (EmaIncline.ValueBool)
                {
                    if (preLastEmaShort <= lastEmaShort)
                    {
                        return;
                    }
                }

                decimal quantity = GetVolume();

                TabToTrade.SellAtLimit(quantity, lastCandleClose - lastCandleClose * (Slippage.ValueDecimal / 100));
                //TabToTrade.SellAtMarket(quantity);
                minLastEmaShort = 1000000000;
            }
        }
        private void LogicClosePosition(Position pos)
        {
            if (pos.State != PositionStateType.Open
                ||
                (pos.CloseOrders != null
                && pos.CloseOrders.Count > 0))
            {
                return;
            }

            if (openPoces[0].Direction == Side.Buy)
            {
                if (lastEmaShort < maxLastEmaShort * (1.0m - drawndownPercentDec) || lastEmaShort < lastEmaLong)
                {
                    TabToTrade.CloseAtLimit(pos, lastCandleClose - lastCandleClose * (Slippage.ValueDecimal / 100), pos.OpenVolume);
                    //TabToTrade.CloseAtMarket(pos, pos.OpenVolume);
                }
            }
            else if (openPoces[0].Direction == Side.Sell)
            {
                if (lastEmaShort > minLastEmaShort * (1.0m + drawndownPercentDec) || lastEmaShort > lastEmaLong)
                {
                    TabToTrade.CloseAtLimit(pos, lastCandleClose + lastCandleClose * (Slippage.ValueDecimal / 100), pos.OpenVolume);
                    //TabToTrade.CloseAtMarket(pos, pos.OpenVolume);
                }
            }
        }

        public override string GetNameStrategyType()
        {
            return "MySuperDoubleEmaBot";
        }

        public override void ShowIndividualSettingsDialog()
        {
            //throw new NotImplementedException();
        }
    }
}

