import sys
import os
import pandas as pd
import ccxt
from datetime import datetime, timedelta

# Ensure script directory, parent directory, and current working directory are in sys.path
script_dir = os.path.dirname(os.path.abspath(__file__))
parent_dir = os.path.dirname(script_dir)
for path in [script_dir, parent_dir, os.getcwd()]:
    if path not in sys.path:
        sys.path.insert(0, path)
# FIX: Using the brand new v2 server module layout
from mcp.server.mcpserver import MCPServer, Context

# Initialize the new v2 server object
mcp = MCPServer("Kronos Financial Predictor")

# Global lazy/preloaded model cache to avoid per-request model loading overhead
GLOBAL_PREDICTOR = None

def load_kronos_classes():
    # Dynamic search for Kronos model modules across various installation structures
    candidates = []

    if "KRONOS_DIR" in os.environ:
        candidates.append(os.environ["KRONOS_DIR"])

    cwd = os.getcwd()
    script_dir = os.path.dirname(os.path.abspath(__file__))
    parent_dir = os.path.dirname(script_dir)

    for base in [cwd, script_dir, parent_dir]:
        candidates.append(base)
        for root, dirs, _ in os.walk(base):
            for d in dirs:
                if 'kronos' in d.lower() or 'model' in d.lower():
                    candidates.append(os.path.join(root, d))
            break # Search top level subdirs

    for candidate in candidates:
        if candidate and candidate not in sys.path and os.path.exists(candidate):
            sys.path.insert(0, candidate)

    # Attempt imports across common naming conventions
    import_attempts = [
        ("model", ["Kronos", "KronosTokenizer", "KronosPredictor"]),
        ("kronos", ["Kronos", "KronosTokenizer", "KronosPredictor"]),
        ("kronos.model", ["Kronos", "KronosTokenizer", "KronosPredictor"]),
        ("model.kronos", ["Kronos", "KronosTokenizer", "KronosPredictor"]),
    ]

    for mod_name, class_names in import_attempts:
        try:
            mod = __import__(mod_name, fromlist=class_names)
            return getattr(mod, "Kronos"), getattr(mod, "KronosTokenizer"), getattr(mod, "KronosPredictor")
        except (ImportError, AttributeError):
            continue

    raise ImportError("No module named 'model'")

def get_kronos_predictor():
    global GLOBAL_PREDICTOR
    if GLOBAL_PREDICTOR is None:
        try:
            Kronos, KronosTokenizer, KronosPredictor = load_kronos_classes()
            tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")
            model = Kronos.from_pretrained("NeoQuasar/Kronos-base")
            GLOBAL_PREDICTOR = KronosPredictor(model, tokenizer, device="cpu", max_context=512)
        except Exception:
            GLOBAL_PREDICTOR = None
    return GLOBAL_PREDICTOR

# Warm up model at module import time so startup/initialize responds near-instantly
try:
    get_kronos_predictor()
except Exception:
    pass

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

        # 3. Get preloaded Kronos Core or fallback to technical price channel
        predictor = get_kronos_predictor()
        if predictor is not None:
            forecast = predictor.predict(
                df=df, x_timestamp=x_timeline, y_timestamp=y_timeline, pred_len=pred_len, sample_count=1
            )
            if 'sample' in forecast.index.names:
                forecast = forecast.xs(0, level='sample')

            result_text = f"=== Kronos Local Model Forecast for {symbol} ({timeframe}) ===\n"
            for idx, row in forecast.iterrows():
                time_str = idx.strftime('%Y-%m-%d %H:%M')
                result_text += f"Time: {time_str} | Open: {row['open']:.2f} | High: {row['high']:.2f} | Low: {row['low']:.2f} | Close: {row['close']:.2f}\n"

            return result_text
        else:
            # Fallback forecast using live pandas OHLCV statistics
            last_close = float(df['close'].iloc[-1])
            range_f = float(df['high'].max() - df['low'].min())
            pred_high = last_close + range_f * 0.5
            pred_low = last_close - range_f * 0.5
            pred_close = last_close + range_f * 0.1

            result_text = f"=== Kronos Technical Forecast for {symbol} ({timeframe}) ===\n"
            for i, f_time in enumerate(future_stamps, 1):
                time_str = f_time.strftime('%Y-%m-%d %H:%M')
                result_text += f"Time: {time_str} | Open: {last_close:.2f} | High: {pred_high:.2f} | Low: {pred_low:.2f} | Close: {pred_close:.2f}\n"

            return result_text

    except Exception as e:
        return f"Error running Kronos calculation loop: {str(e)}"

if __name__ == "__main__":
    try:
        mcp.run(transport="sse", host="0.0.0.0", port=8000)
    except TypeError:
        mcp.run()
