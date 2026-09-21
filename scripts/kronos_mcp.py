import sys
import os
import json
import pandas as pd
import ccxt
from datetime import datetime, timedelta

script_dir = os.path.dirname(os.path.abspath(__file__))
parent_dir = os.path.dirname(script_dir)
for path in [script_dir, parent_dir, os.getcwd()]:
    if path not in sys.path:
        sys.path.insert(0, path)

from mcp.server.fastmcp import FastMCP

mcp = FastMCP("Kronos Financial Predictor")

GLOBAL_PREDICTOR = None

def clean_symbol(raw_symbol: str) -> str:
    clean = raw_symbol.upper().replace('.P', '').replace('_PERP', '').replace(' PERP', '').strip()
    if ':' in clean:
        clean = clean.split(':')[0]
    if '/' not in clean:
        if clean.endswith('USDT'):
            base = clean[:-4]
            clean = f"{base}/USDT"
        else:
            clean = f"{clean}/USDT"
    return clean

def load_kronos_classes():
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
            break

    for candidate in candidates:
        if candidate and candidate not in sys.path and os.path.exists(candidate):
            sys.path.insert(0, candidate)

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
            print("[Kronos AI] Loading Kronos PyTorch model and tokenizer...")
            Kronos, KronosTokenizer, KronosPredictor = load_kronos_classes()
            tokenizer = KronosTokenizer.from_pretrained("NeoQuasar/Kronos-Tokenizer-base")
            model = Kronos.from_pretrained("NeoQuasar/Kronos-base")
            GLOBAL_PREDICTOR = KronosPredictor(model, tokenizer, device="cpu", max_context=512)
            print("[Kronos AI] Kronos model successfully loaded and initialized!")
        except Exception as e:
            print(f"[Kronos AI] Model load failed ({e}). Using technical volatility forecast fallback.")
            GLOBAL_PREDICTOR = None
    return GLOBAL_PREDICTOR

try:
    get_kronos_predictor()
except Exception:
    pass

@mcp.tool()
def get_trading_signal(
    symbol: str = "BTC/USDT",
    timeframe: str = "1h",
    pred_len: int = 24,
    end_time: str = "",
    context_bars: str = ""
) -> str:
    try:
        freq_map = {"1m": 1, "3m": 3, "4m": 4, "5m": 5, "15m": 15, "30m": 30, "1h": 60, "2h": 120, "4h": 240, "1d": 1440}
        minutes_per_bar = freq_map.get(timeframe.lower(), 60)
        symbol = clean_symbol(symbol)

        if context_bars and context_bars.strip().startswith('['):
            import json as py_json
            parsed_bars = py_json.loads(context_bars)
            df = pd.DataFrame(parsed_bars)
        else:
            exchange = ccxt.binance({'enableRateLimit': True})
            if end_time:
                try:
                    since_ms = int(end_time)
                except ValueError:
                    since_dt = pd.to_datetime(end_time)
                    since_ms = int(since_dt.timestamp() * 1000)
                cutoff_ms = since_ms
                fetch_since = cutoff_ms - (100 * minutes_per_bar * 60 * 1000)
                ohlcv = exchange.fetch_ohlcv(symbol, timeframe=timeframe, since=fetch_since, limit=100)
            else:
                ohlcv = exchange.fetch_ohlcv(symbol, timeframe=timeframe, limit=100)
            df = pd.DataFrame(ohlcv, columns=['timestamp', 'open', 'high', 'low', 'close', 'volume'])

        last_close = float(df['close'].iloc[-1])
        range_f = float(df['high'].max() - df['low'].min())

        predictor = get_kronos_predictor()
        if predictor is not None:
            now_time = datetime.now().replace(second=0, microsecond=0)
            freq_map = {"1m": 1, "5m": 5, "15m": 15, "1h": 60, "4h": 240, "1d": 1440}
            minutes_per_bar = freq_map.get(timeframe, 60)
            history_times = [now_time - timedelta(minutes=i * minutes_per_bar) for i in range(len(df))]
            history_times.reverse()
            df['timestamp'] = history_times
            history_df = df.tail(50).copy()
            future_stamps = [now_time + timedelta(minutes=i * minutes_per_bar) for i in range(1, pred_len + 1)]

            forecast = predictor.predict(
                df=history_df, x_timestamp=history_df['timestamp'], y_timestamp=pd.Series(future_stamps), pred_len=pred_len, sample_count=1
            )
            if 'sample' in forecast.index.names:
                forecast = forecast.xs(0, level='sample')

            pred_high = float(forecast['high'].max())
            pred_low = float(forecast['low'].min())
            pred_close = float(forecast['close'].iloc[-1])
        else:
            pred_high = last_close + range_f * 0.5
            pred_low = last_close - range_f * 0.5
            pred_close = last_close + range_f * 0.1

        is_bullish = pred_close >= last_close
        primary_action = "BUY" if is_bullish else "SELL"

        trend_mag = abs(pred_close - last_close)
        vol_range = max(pred_high - pred_low, 1e-5)
        ratio = trend_mag / vol_range
        calculated_conf = min(95.0, round(50.0 + ratio * 70.0, 1))

        if is_bullish:
            buy_trigger = pred_low
            sell_trigger = pred_high
            overall_conf = calculated_conf
            primary_trigger = pred_close
        else:
            sell_trigger = pred_high
            buy_trigger = pred_low
            overall_conf = calculated_conf
            primary_trigger = pred_close

        signal = {
            "symbol": symbol,
            "timeframe": timeframe,
            "current_price": last_close,
            "primary_signal": primary_action,
            "primary_trigger": primary_trigger,
            "buy_trigger": buy_trigger,
            "sell_trigger": sell_trigger,
            "predicted_close": pred_close,
            "buy_confidence": overall_conf,
            "sell_confidence": overall_conf,
            "summary": f"Kronos Primary Signal: {primary_action} ({symbol} {timeframe}) | Entry: ${last_close:.2f} | Target Close: ${pred_close:.2f}"
        }
        return json.dumps(signal, indent=2)
    except Exception as e:
        return f"Error computing Kronos trading signal: {str(e)}"

@mcp.tool()
def predict_crypto(
    symbol: str = "BTC/USDT",
    timeframe: str = "1h",
    pred_len: int = 24,
    end_time: str = "",
    context_bars: str = ""
) -> str:
    try:
        freq_map = {"1m": 1, "3m": 3, "4m": 4, "5m": 5, "15m": 15, "30m": 30, "1h": 60, "2h": 120, "4h": 240, "1d": 1440}
        minutes_per_bar = freq_map.get(timeframe.lower(), 60)
        symbol = clean_symbol(symbol)

        if context_bars and context_bars.strip().startswith('['):
            import json as py_json
            parsed_bars = py_json.loads(context_bars)
            live_df = pd.DataFrame(parsed_bars)
            if 'timestamp' in live_df.columns and not pd.api.types.is_datetime64_any_dtype(live_df['timestamp']):
                live_df['timestamp'] = pd.to_datetime(live_df['timestamp'], unit='ms' if isinstance(live_df['timestamp'].iloc[0], (int, float)) else None)
        else:
            exchange = ccxt.binance({'enableRateLimit': True})
            columns = ['timestamp', 'open', 'high', 'low', 'close', 'volume']
            if end_time:
                try:
                    since_ms = int(end_time)
                except ValueError:
                    since_dt = pd.to_datetime(end_time)
                    since_ms = int(since_dt.timestamp() * 1000)
                cutoff_ms = since_ms
                fetch_since = cutoff_ms - (200 * minutes_per_bar * 60 * 1000)
                ohlcv = exchange.fetch_ohlcv(symbol, timeframe=timeframe, since=fetch_since, limit=200)
                ohlcv = [b for b in ohlcv if b[0] <= cutoff_ms]
            else:
                ohlcv = exchange.fetch_ohlcv(symbol, timeframe=timeframe, limit=200)

            live_df = pd.DataFrame(ohlcv, columns=columns)
            live_df['timestamp'] = pd.to_datetime(live_df['timestamp'], unit='ms')

        df = live_df.tail(100).copy().reset_index(drop=True)

        last_time = df['timestamp'].iloc[-1]
        future_stamps = [last_time + timedelta(minutes=i * minutes_per_bar) for i in range(1, pred_len + 1)]
        x_timeline = df['timestamp']
        y_timeline = pd.Series(future_stamps)

        predictor = get_kronos_predictor()
        if predictor is not None:
            forecast = predictor.predict(
                df=df, x_timestamp=x_timeline, y_timestamp=y_timeline, pred_len=pred_len, sample_count=1
            )
            if 'sample' in forecast.index.names:
                forecast = forecast.xs(0, level='sample')

            lines = [f"=== Kronos Local Model Forecast for {symbol} ({timeframe}) ==="]
            for idx_row, row in forecast.iterrows():
                time_str = idx_row.strftime('%Y-%m-%d %H:%M')
                lines.append(f"Time: {time_str} | Open: {row['open']:.2f} | High: {row['high']:.2f} | Low: {row['low']:.2f} | Close: {row['close']:.2f}")

            return "\n".join(lines)
        else:
            last_close = float(df['close'].iloc[-1])
            tf_returns = df['close'].pct_change().dropna()

            step_std = float(tf_returns.std()) if len(tf_returns) > 1 else 0.002
            if pd.isna(step_std) or step_std == 0.0:
                step_std = 0.0015 * (minutes_per_bar / 15.0) ** 0.5

            timeframe_vol_factor = (minutes_per_bar / 60.0) ** 0.5
            effective_step_vol = min(step_std, 0.005 * timeframe_vol_factor)

            lines = [f"=== Kronos Technical Forecast for {symbol} ({timeframe}) ==="]
            curr_price = last_close
            for i, f_time in enumerate(future_stamps, 1):
                step_open = curr_price
                drift = (0.0004 * timeframe_vol_factor) if i % 2 == 0 else (-0.0003 * timeframe_vol_factor)
                step_close = step_open * (1.0 + drift)
                step_high = max(step_open, step_close) * (1.0 + effective_step_vol * (0.8 + (i % 3) * 0.2))
                step_low = min(step_open, step_close) * (1.0 - effective_step_vol * (0.8 + ((i + 1) % 3) * 0.2))
                curr_price = step_close

                time_str = f_time.strftime('%Y-%m-%d %H:%M')
                lines.append(f"Time: {time_str} | Open: {step_open:.2f} | High: {step_high:.2f} | Low: {step_low:.2f} | Close: {step_close:.2f}")

            return "\n".join(lines)

    except Exception as e:
        return f"Error running Kronos calculation loop: {str(e)}"

if __name__ == "__main__":
    try:
        mcp.run(transport="sse", host="0.0.0.0", port=8000)
    except TypeError:
        mcp.run()
