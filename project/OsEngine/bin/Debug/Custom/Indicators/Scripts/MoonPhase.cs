using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace CustomIndicators.Scripts
{
    internal class MoonPhase : Aindicator
    {
        private IndicatorDataSeries _fullMoonSeries;
        private IndicatorDataSeries _newMoonSeries;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _fullMoonSeries = CreateSeries("Full Moon", Color.Yellow, IndicatorChartPaintType.Column, true);
                _newMoonSeries = CreateSeries("New Moon", Color.Black, IndicatorChartPaintType.Column, true);
            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            DateTime currentDate = candles[index].TimeStart;
            double daysSinceNewMoon = (currentDate - new DateTime(2000, 1, 6)).TotalDays;

            // Расчет фазы Луны
            double lunarCycle = 29.53;
            double phase = daysSinceNewMoon % lunarCycle;

            if (phase < 1) // Новолуние
            {
                _newMoonSeries.Values[index] = candles[index].Close; // Отметка новолуния
                _fullMoonSeries.Values[index] = 0; // Удаляем отметку полнолуния
            }
            else if (phase > 14 && phase < 16) // Полнолуние
            {
                _fullMoonSeries.Values[index] = candles[index].Close; // Отметка полнолуния
                _newMoonSeries.Values[index] = 0; // Удаляем отметку новолуния
            }
            else
            {
                _newMoonSeries.Values[index] = 0; // Нет отметки новолуния
                _fullMoonSeries.Values[index] = 0; // Нет отметки полнолуния
            }
        }
    }
}
