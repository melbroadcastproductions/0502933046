import sys
import pandas as pd
import ccxt
from datetime import datetime, timedelta
# FIX: Using the brand new v2 server module layout
from mcp.server.mcpserver import MCPServer, Context

# Initialize the new v2 server object
mcp = MCPServer("Kronos Financial Predictor")

# Load Kronos dependencies inside the server wrapper
from model import Kronos, KronosTokenizer, KronosPredictor

@mcp.tool()
def predict_crypto(symbol: str = "BTC/USDT", timeframe: str = "1h", pred_len: int = 24) -> str:
    """
    Fetches live market data from Binance and runs the local Kronos Time Series model
    to generate future Max, Min, and Close price predictions.
    """
    try:
        # 1. Fetch live data from Binance
        exchange = ccxt.binance({'enableRateLimit': True})
        ohlcv = exchange.fetch_ohlcv(symbol, timeframe=timeframe, limit=100)

        columns = ['timestamp', 'open', 'high', 'low', 'close', 'volume']
        live_df = pd.DataFrame(ohlcv, columns=columns)

        # 2. Align timestamps to local clock
        now_time = datetime.now().replace(second=0, microsecond=0)
        freq_map = {"1m": 1, "5m": 5, "15m": 15, "1h": 60, "4h": 240, "1d": 1440}
        minutes_per_bar = freq_map.get(timeframe, 60)

        history_times = [now_time - timedelta(minutes=i * minutes_per_bar) for i in range(len(live_df))]
        history_times.reverse()
        live_df['timestamp'] = history_times
        df = live_df.tail(50).copy()

        future_stamps = [now_time + timedelta(minutes=i * minutes_per_bar) for i in range(1, pred_len + 1)]
        x_timeline = df['timestamp']
        y_timeline = pd.Series(future_stamps)

        # 3. Load Kronos Core
        tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")
        model = Kronos.from_pretrained("NeoQuasar/Kronos-base")
        predictor = KronosPredictor(model, tokenizer, device="cpu", max_context=512)

        # 4. Predict
        forecast = predictor.predict(
            df=df, x_timestamp=x_timeline, y_timestamp=y_timeline, pred_len=pred_len, sample_count=1
        )
        if 'sample' in forecast.index.names:
            forecast = forecast.xs(0, level='sample')

        # 5. Format results cleanly for Claude and Flowsurface
        result_text = f"=== Kronos Local Model Forecast for {symbol} ({timeframe}) ===\n"
        for idx, row in forecast.iterrows():
            time_str = idx.strftime('%Y-%m-%d %H:%M')
            result_text += f"Time: {time_str} | Open: {row['open']:.2f} | High: {row['high']:.2f} | Low: {row['low']:.2f} | Close: {row['close']:.2f}\n"

        return result_text
    except Exception as e:
        return f"Error running Kronos calculation loop: {str(e)}"

if __name__ == "__main__":
    mcp.run()
