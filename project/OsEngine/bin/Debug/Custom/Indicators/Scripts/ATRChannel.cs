using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace CustomIndicators.Scripts
{
    class ATRChannel : Aindicator
    {
        private IndicatorParameterInt _length;
        //private IndicatorParameterDecimal _deviation;

        //private IndicatorParameterString _typeSeries;

        private IndicatorDataSeries _seriesUp;
        private IndicatorDataSeries _seriesDown;
        private IndicatorDataSeries _seriesCenter;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _length = CreateParameterInt("Length", 14);
                //_deviation = CreateParameterDecimal("Deviation", 2);
                //_typeSeries = CreateParameterStringCollection("Series type",
                //    "Absolute",
                //    new List<string>() { "Absolute", "Percent" });

                _seriesUp = CreateSeries("Up line", Color.DarkRed, IndicatorChartPaintType.Line, true);
                _seriesCenter = CreateSeries("Centre line", Color.Green, IndicatorChartPaintType.Line, true); //было false
                _seriesDown = CreateSeries("Down line", Color.DarkKhaki, IndicatorChartPaintType.Line, true);

            }
            else if (state == IndicatorState.Dispose)
            {
                if (_moving != null)
                {
                    _moving.Clear();
                }
                if (_trueRange != null)
                {
                    _trueRange.Clear();
                }
            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {

            TrueRangeReload(candles, index);
            _moving = MovingAverageWild(_trueRange, _moving, _length.ValueInt, index);

            decimal valueATR = Math.Round(_moving[index], 9);

            _seriesCenter.Values[index] = valueATR;

            if (index >= _length.ValueInt)
            {
                decimal valueUp = 0;
                decimal valueDown = 9999999999;

                for (int i = 0; i < _length.ValueInt; i++)
                {
                    if (_seriesCenter.Values[index - i] > valueUp)
                    {
                        valueUp = _seriesCenter.Values[index - i];
                    }

                    if (_seriesCenter.Values[index - i] < valueDown)
                    {
                        valueDown = _seriesCenter.Values[index - i];
                    }
                }
                _seriesUp.Values[index] = valueUp;
                _seriesDown.Values[index] = valueDown;
            }
            //else
            //{
            //    _seriesUp.Values[index] = 0;
            //    _seriesDown.Values[index] = 0;
            //}
        }

        private List<decimal> _moving = new List<decimal>();

        private List<decimal> _trueRange = new List<decimal>();

        private void TrueRangeReload(List<Candle> candles, int index)
        {
            if (index == 0)
            {
                _trueRange = new List<decimal>();
                _trueRange.Add(0);
                return;
            }

            while (_trueRange.Count - 1 < index)
            {
                _trueRange.Add(0);
            }

            decimal hiToLow = Math.Abs(candles[index].High - candles[index].Low);
            decimal closeToHigh = Math.Abs(candles[index - 1].Close - candles[index].High);
            decimal closeToLow = Math.Abs(candles[index - 1].Close - candles[index].Low);

            decimal value = Math.Max(Math.Max(hiToLow, closeToHigh), closeToLow);

            //if (value != 0
            //    && candles[index - 1].Open != 0)
            //{
            //    value = value / (candles[index - 1].Open / 100);
            //}

            _trueRange[index] = value;
        }

        private List<decimal> MovingAverageWild(List<decimal> valuesSeries, List<decimal> moving, int length, int index)
        {
            if (moving == null || length > valuesSeries.Count)
            {
                moving = new List<decimal>();
                for (int i = 0; i < index + 1; i++)
                {
                    moving.Add(0);
                }
            }
            else if (length == valuesSeries.Count)
            {
                decimal lastMoving = 0;

                for (int i = index; i > -1 && i > valuesSeries.Count - 1 - length; i--)
                {
                    lastMoving += valuesSeries[i];
                }

                if (lastMoving != 0)
                {
                    moving.Add(lastMoving / length);
                }
                else
                {
                    moving.Add(0);
                }
            }
            else
            {
                while (moving.Count < index)
                {
                    moving.Add(moving[moving.Count - 1]);
                }

                decimal lastValueSeries = Math.Round(valuesSeries[index], 9);
                decimal lastValueMoving = moving[index - 1];

                if (index > moving.Count - 1)
                {
                    moving.Add(0);
                }
                moving[index] = Math.Round((lastValueMoving * (_length.ValueInt - 1) + lastValueSeries) / _length.ValueInt, 9);
            }

            return moving;
        }
    }
}