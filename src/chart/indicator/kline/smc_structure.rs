//! SMC overlay: swing pivot + BOS/CHoCH structure, FVG boxes, swing
//! HH/HL/LH/LL tags, and Premium/Discount/Equilibrium zones, ported from
//! `SMC Structure + FVG + Liquidity [morning]` (Pine v23).
//!
//! Unlike the other kline indicators (Volume, CVD, OpenInterest), this
//! does NOT implement `KlineIndicatorImpl` / render as a sub-pane. It
//! draws directly onto the main price canvas, the same way
//! `draw_all_npocs` / `draw_clusters` do in `chart/kline.rs`. Wire it in
//! by calling the `draw_*` functions from `KlineChart::draw()`, right
//! before `chart.draw_last_price_line(...)`.

use exchange::Kline;
use exchange::unit::Price;
use iced::theme::palette::Extended;
use iced::widget::canvas::{self, Path, Stroke, Text};
use iced::{Color, Point, Size};

/// Direction of a structure break / FVG gap.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Direction {
    Bullish,
    Bearish,
}

/// BOS = break of structure (continuation), CHoCH = change of character (reversal).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BreakKind {
    Bos,
    Choch,
}

/// One confirmed structure level, ready to draw.
#[derive(Debug, Clone)]
pub struct StructureLevel {
    pub price: Price,
    /// Time of the swing point that was broken (left edge of the line).
    pub from_time: u64,
    /// Time of the bar that confirmed the break (right edge of the line).
    pub confirmed_time: u64,
    pub direction: Direction,
    pub kind: BreakKind,
    /// 1..=5, mirrors the Pine script's `f_rating` (leg size vs. rolling avg,
    /// +1 if volume boosted).
    pub rating: u8,
}

/// HH/HL/LH/LL classification for a confirmed swing pivot.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SwingTagKind {
    HigherHigh,
    LowerHigh,
    HigherLow,
    LowerLow,
}

impl SwingTagKind {
    fn label(self) -> &'static str {
        match self {
            SwingTagKind::HigherHigh => "HH",
            SwingTagKind::LowerHigh => "LH",
            SwingTagKind::HigherLow => "HL",
            SwingTagKind::LowerLow => "LL",
        }
    }

    fn is_high(self) -> bool {
        matches!(self, SwingTagKind::HigherHigh | SwingTagKind::LowerHigh)
    }
}

#[derive(Debug, Clone)]
pub struct SwingTagEntry {
    pub price: Price,
    pub time: u64,
    pub kind: SwingTagKind,
}

/// One fair value gap box.
#[derive(Debug, Clone)]
pub struct FvgZone {
    pub top: Price,
    pub bottom: Price,
    pub start_time: u64,
    /// Right edge of the box: either the fill time, or `None` if still
    /// unfilled (caller extends it to the current visible right edge).
    pub end_time: Option<u64>,
    pub direction: Direction,
    pub rating: u8,
}

/// Latest Premium / Equilibrium / Discount levels over the rolling range
/// lookback, smoothed the same way the Pine script does.
#[derive(Debug, Clone, Copy)]
pub struct ZoneLevels {
    pub premium: Price,
    pub equilibrium: Price,
    pub discount: Price,
}

#[derive(Debug, Clone, Copy)]
struct SwingPoint {
    price: f64,
    time: u64,
}

/// Rolling detection state, equivalent to the Pine script's `var` state
/// (lastSH/lastSL/trendDir/legAvg/prevPH/prevPL). Owned by the
/// indicator/chart and rebuilt from the current kline series.
#[derive(Debug, Clone, Default)]
pub struct SmcStructureState {
    pub levels: Vec<StructureLevel>,
    pub swing_tags: Vec<SwingTagEntry>,
    pub fvgs: Vec<FvgZone>,
    pub zones: Option<ZoneLevels>,
    last_swing_high: Option<SwingPoint>,
    last_swing_low: Option<SwingPoint>,
    trend_dir: i8, // -1, 0, 1
    leg_avg: Option<f64>,
    max_levels: usize,
}

impl SmcStructureState {
    pub fn new(max_levels: usize) -> Self {
        Self {
            max_levels,
            ..Default::default()
        }
    }

    /// Full recompute from an ordered slice of klines. Mirrors the Pine
    /// script's swing-pivot + BOS/CHoCH loop (`ta.pivothigh/pivotlow` with
    /// `swingLen` bars of lookback/lookahead on each side), plus FVG
    /// detection and the Premium/Discount/Equilibrium zone calculation.
    pub fn rebuild(&mut self, klines: &[Kline], swing_len: usize, vol_boost: f64) {
        *self = Self::new(self.max_levels.max(40));

        if klines.len() < swing_len * 2 + 1 {
            return;
        }

        let avg_vol: f64 = {
            let n = klines.len().min(20);
            let sum: f64 = klines[klines.len() - n..]
                .iter()
                .map(|k| k.volume.total().to_f64())
                .sum();
            if n > 0 { sum / n as f64 } else { 0.0 }
        };

        let mut prev_swing_high: Option<f64> = None;
        let mut prev_swing_low: Option<f64> = None;

        for i in swing_len..klines.len() - swing_len {
            // Pivot high: klines[i].high is the max within the [i-swing_len, i+swing_len] window.
            let window = &klines[i - swing_len..=i + swing_len];
            let center = klines[i];

            let is_pivot_high = window.iter().all(|k| k.high <= center.high);
            let is_pivot_low = window.iter().all(|k| k.low >= center.low);

            if is_pivot_high {
                let price = center.high.to_f64();
                let time = center.time.as_u64();

                if let Some(prev) = prev_swing_high {
                    let kind = if price > prev {
                        SwingTagKind::HigherHigh
                    } else {
                        SwingTagKind::LowerHigh
                    };
                    self.swing_tags.push(SwingTagEntry {
                        price: center.high,
                        time,
                        kind,
                    });
                }
                prev_swing_high = Some(price);
                self.last_swing_high = Some(SwingPoint { price, time });
            }
            if is_pivot_low {
                let price = center.low.to_f64();
                let time = center.time.as_u64();

                if let Some(prev) = prev_swing_low {
                    let kind = if price > prev {
                        SwingTagKind::HigherLow
                    } else {
                        SwingTagKind::LowerLow
                    };
                    self.swing_tags.push(SwingTagEntry {
                        price: center.low,
                        time,
                        kind,
                    });
                }
                prev_swing_low = Some(price);
                self.last_swing_low = Some(SwingPoint { price, time });
            }

            // Break checks use the *closing* price of bars after the pivot,
            // same as `close > lastSH` / `close < lastSL` in Pine.
            let confirm = klines[i];
            let close = confirm.close.to_f64();
            let confirm_time = confirm.time.as_u64();
            let confirm_vol = confirm.volume.total().to_f64();

            if let (Some(sh), Some(sl)) = (self.last_swing_high, self.last_swing_low) {
                if close > sh.price && confirm_time > sh.time {
                    self.push_break(sh, sl, Direction::Bullish, confirm_time, avg_vol, confirm_vol, vol_boost);
                    self.last_swing_high = None;
                } else if close < sl.price && confirm_time > sl.time {
                    self.push_break(sl, sh, Direction::Bearish, confirm_time, avg_vol, confirm_vol, vol_boost);
                    self.last_swing_low = None;
                }
            }
        }

        self.rebuild_fvgs(klines, 0.25);
        self.rebuild_zones(klines, 60, 10);
    }

    fn push_break(
        &mut self,
        broken: SwingPoint,
        opposite: SwingPoint,
        direction: Direction,
        confirmed_time: u64,
        avg_vol: f64,
        confirm_vol: f64,
        vol_boost_mult: f64,
    ) {
        let leg_size = (broken.price - opposite.price).abs();
        let basis = self.leg_avg.filter(|a| *a > 0.0).unwrap_or(leg_size);
        let ratio = if basis > 0.0 { leg_size / basis } else { 1.0 };

        let mut rating: u8 = if ratio >= 1.60 {
            5
        } else if ratio >= 1.25 {
            4
        } else if ratio >= 0.90 {
            3
        } else if ratio >= 0.60 {
            2
        } else {
            1
        };
        if avg_vol > 0.0 && confirm_vol > avg_vol * vol_boost_mult {
            rating = (rating + 1).min(5);
        }

        self.leg_avg = Some(match self.leg_avg {
            Some(a) => a * 0.9 + leg_size * 0.1,
            None => leg_size,
        });

        let is_choch = match direction {
            Direction::Bullish => self.trend_dir == -1,
            Direction::Bearish => self.trend_dir == 1,
        };

        self.levels.push(StructureLevel {
            price: Price::from_f64(broken.price),
            from_time: broken.time,
            confirmed_time,
            direction,
            kind: if is_choch { BreakKind::Choch } else { BreakKind::Bos },
            rating,
        });

        self.trend_dir = match direction {
            Direction::Bullish => 1,
            Direction::Bearish => -1,
        };

        if self.levels.len() > self.max_levels {
            self.levels.remove(0);
        }
    }

    /// FVG detection: a 3-bar gap where `low[i] > high[i-2]` (bullish) or
    /// `high[i] < low[i-2]` (bearish), sized at least `min_pct` of the
    /// rolling 20-bar average range. Filled boxes (price has traded back
    /// through them) are dropped, matching the Pine script deleting them.
    fn rebuild_fvgs(&mut self, klines: &[Kline], min_pct: f64) {
        if klines.len() < 21 {
            return;
        }

        let avg_range = |end: usize| -> f64 {
            let start = end.saturating_sub(20);
            let n = end - start;
            if n == 0 {
                return 0.0;
            }
            let sum: f64 = klines[start..end]
                .iter()
                .map(|k| k.high.to_f64() - k.low.to_f64())
                .sum();
            sum / n as f64
        };

        let mut fvg_size_avg: Option<f64> = None;
        let mut candidates: Vec<FvgZone> = Vec::new();

        for i in 2..klines.len() {
            let min_gap = avg_range(i) * min_pct;
            let hi2 = klines[i - 2].high.to_f64();
            let lo2 = klines[i - 2].low.to_f64();
            let hi = klines[i].high.to_f64();
            let lo = klines[i].low.to_f64();
            let start_time = klines[i - 2].time.as_u64();

            if lo > hi2 && (lo - hi2) >= min_gap && min_gap > 0.0 {
                let size = lo - hi2;
                let rating = fvg_rating(size, fvg_size_avg);
                fvg_size_avg = Some(match fvg_size_avg {
                    Some(a) => a * 0.9 + size * 0.1,
                    None => size,
                });
                candidates.push(FvgZone {
                    top: Price::from_f64(lo),
                    bottom: Price::from_f64(hi2),
                    start_time,
                    end_time: None,
                    direction: Direction::Bullish,
                    rating,
                });
            } else if hi < lo2 && (lo2 - hi) >= min_gap && min_gap > 0.0 {
                let size = lo2 - hi;
                let rating = fvg_rating(size, fvg_size_avg);
                fvg_size_avg = Some(match fvg_size_avg {
                    Some(a) => a * 0.9 + size * 0.1,
                    None => size,
                });
                candidates.push(FvgZone {
                    top: Price::from_f64(lo2),
                    bottom: Price::from_f64(hi),
                    start_time,
                    end_time: None,
                    direction: Direction::Bearish,
                    rating,
                });
            }

            // Check fills for still-open candidates against this bar.
            for fvg in candidates.iter_mut() {
                if fvg.end_time.is_some() || klines[i].time.as_u64() <= fvg.start_time {
                    continue;
                }
                let filled = match fvg.direction {
                    Direction::Bullish => lo <= fvg.bottom.to_f64(),
                    Direction::Bearish => hi >= fvg.top.to_f64(),
                };
                if filled {
                    fvg.end_time = Some(klines[i].time.as_u64());
                }
            }
        }

        // Keep only unfilled boxes (still relevant / worth drawing), most
        // recent last, capped the same way structure levels are.
        candidates.retain(|f| f.end_time.is_none());
        if candidates.len() > 30 {
            let excess = candidates.len() - 30;
            candidates.drain(0..excess);
        }
        self.fvgs = candidates;
    }

    /// Premium / Discount / Equilibrium zone levels: rolling `zone_len`-bar
    /// high/low range, split 75/50/25%, smoothed over the last
    /// `zone_smooth` bars - same as the Pine script's `premR`/`eqR`/`discR`.
    fn rebuild_zones(&mut self, klines: &[Kline], zone_len: usize, zone_smooth: usize) {
        if klines.len() < zone_len {
            self.zones = None;
            return;
        }

        let n = klines.len();
        let tail = zone_smooth.min(n);
        let mut prem_sum = 0.0;
        let mut disc_sum = 0.0;
        let mut eq_sum = 0.0;

        for offset in 0..tail {
            let end = n - offset;
            let start = end.saturating_sub(zone_len);
            if end <= start {
                continue;
            }
            let window = &klines[start..end];
            let hi = window
                .iter()
                .map(|k| k.high.to_f64())
                .fold(f64::MIN, f64::max);
            let lo = window
                .iter()
                .map(|k| k.low.to_f64())
                .fold(f64::MAX, f64::min);

            prem_sum += lo + (hi - lo) * 0.75;
            disc_sum += lo + (hi - lo) * 0.25;
            eq_sum += (hi + lo) / 2.0;
        }

        if tail == 0 {
            self.zones = None;
            return;
        }

        self.zones = Some(ZoneLevels {
            premium: Price::from_f64(prem_sum / tail as f64),
            equilibrium: Price::from_f64(eq_sum / tail as f64),
            discount: Price::from_f64(disc_sum / tail as f64),
        });
    }
}

fn fvg_rating(size: f64, rolling_avg: Option<f64>) -> u8 {
    let basis = rolling_avg.filter(|a| *a > 0.0).unwrap_or(size);
    let ratio = if basis > 0.0 { size / basis } else { 1.0 };
    if ratio >= 1.60 {
        5
    } else if ratio >= 1.25 {
        4
    } else if ratio >= 0.90 {
        3
    } else if ratio >= 0.60 {
        2
    } else {
        1
    }
}

/// Draws confirmed BOS/CHoCH levels onto the main price canvas.
pub fn draw_smc_structure(
    frame: &mut canvas::Frame,
    price_to_y: impl Fn(Price) -> f32,
    interval_to_x: impl Fn(u64) -> f32,
    state: &SmcStructureState,
    palette: &Extended,
    visible_earliest: u64,
    visible_latest: u64,
) {
    let bull_color = palette.success.base.color;
    let bear_color = palette.danger.base.color;

    for level in &state.levels {
        if level.confirmed_time < visible_earliest || level.from_time > visible_latest {
            continue;
        }

        let color = match level.direction {
            Direction::Bullish => bull_color,
            Direction::Bearish => bear_color,
        };

        let y = price_to_y(level.price);
        let x1 = interval_to_x(level.from_time);
        let x2 = interval_to_x(level.confirmed_time);

        let line = Path::line(Point::new(x1, y), Point::new(x2, y));
        frame.stroke(&line, Stroke::default().with_color(color).with_width(2.0));

        let label = match level.kind {
            BreakKind::Bos => "BOS ",
            BreakKind::Choch => "CHoCH ",
        };
        let stars = "\u{2605}".repeat(level.rating as usize);
        let text = format!("{label}{stars}");

        let mid_x = (x1 + x2) / 2.0;
        let offset_y = match level.direction {
            Direction::Bullish => y - 14.0,
            Direction::Bearish => y + 4.0,
        };

        frame.fill_text(Text {
            content: text,
            position: Point::new(mid_x, offset_y),
            color: Color::WHITE,
            size: iced::Pixels(11.0),
            ..Text::default()
        });
    }
}

/// Draws HH/HL/LH/LL tags at each confirmed swing pivot.
pub fn draw_swing_tags(
    frame: &mut canvas::Frame,
    price_to_y: impl Fn(Price) -> f32,
    interval_to_x: impl Fn(u64) -> f32,
    state: &SmcStructureState,
    palette: &Extended,
    visible_earliest: u64,
    visible_latest: u64,
) {
    let bull_color = palette.success.base.color;
    let bear_color = palette.danger.base.color;

    for tag in &state.swing_tags {
        if tag.time < visible_earliest || tag.time > visible_latest {
            continue;
        }

        let x = interval_to_x(tag.time);
        let y = price_to_y(tag.price);
        let is_higher = matches!(
            tag.kind,
            SwingTagKind::HigherHigh | SwingTagKind::HigherLow
        );
        let color = if is_higher { bull_color } else { bear_color };
        let offset_y = if tag.kind.is_high() { y - 16.0 } else { y + 6.0 };

        frame.fill_text(Text {
            content: tag.kind.label().to_string(),
            position: Point::new(x, offset_y),
            color,
            size: iced::Pixels(10.0),
            ..Text::default()
        });
    }
}

/// Draws unfilled FVG boxes with their star rating.
pub fn draw_fvg_boxes(
    frame: &mut canvas::Frame,
    price_to_y: impl Fn(Price) -> f32,
    interval_to_x: impl Fn(u64) -> f32,
    state: &SmcStructureState,
    fvg_color: Color,
    visible_earliest: u64,
    visible_latest: u64,
) {
    for fvg in &state.fvgs {
        let right_edge = fvg.end_time.unwrap_or(visible_latest);
        if right_edge < visible_earliest || fvg.start_time > visible_latest {
            continue;
        }

        let x1 = interval_to_x(fvg.start_time);
        let x2 = if fvg.end_time.is_none() {
            interval_to_x(visible_latest)
        } else {
            interval_to_x(right_edge)
        };

        let y_top = price_to_y(fvg.top);
        let y_bottom = price_to_y(fvg.bottom);
        let (top, height) = if y_top <= y_bottom {
            (y_top, y_bottom - y_top)
        } else {
            (y_bottom, y_top - y_bottom)
        };

        let rect = Path::rectangle(Point::new(x1, top), Size::new((x2 - x1).max(1.0), height.max(1.0)));
        frame.fill(&rect, fvg_color.scale_alpha(0.18));
        frame.stroke(
            &rect,
            Stroke::default().with_color(fvg_color.scale_alpha(0.6)).with_width(1.0),
        );

        let stars = "\u{2605}".repeat(fvg.rating as usize);
        frame.fill_text(Text {
            content: format!("FVG {stars}"),
            position: Point::new(x1 + 4.0, top + 2.0),
            color: fvg_color.scale_alpha(1.0),
            size: iced::Pixels(10.0),
            ..Text::default()
        });
    }
}

/// Draws the latest Premium / Equilibrium / Discount zone lines, extending
/// from the current bar rightward - matches the Pine script's
/// `barstate.islast`-only labels.
pub fn draw_zones(
    frame: &mut canvas::Frame,
    price_to_y: impl Fn(Price) -> f32,
    state: &SmcStructureState,
    label_color: Color,
    left_x: f32,
    right_x: f32,
) {
    let Some(zones) = state.zones else { return };

    let rows = [
        ("Premium", zones.premium),
        ("Eq", zones.equilibrium),
        ("Discount", zones.discount),
    ];

    for (label, price) in rows {
        let y = price_to_y(price);
        let line = Path::line(Point::new(left_x, y), Point::new(right_x, y));
        frame.stroke(
            &line,
            Stroke::default()
                .with_color(label_color.scale_alpha(0.5))
                .with_width(1.0),
        );
        frame.fill_text(Text {
            content: label.to_string(),
            position: Point::new(left_x, y - 12.0),
            color: label_color,
            size: iced::Pixels(10.0),
            ..Text::default()
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use exchange::Volume;
    use exchange::unit::{Power10, Qty, UnixMs};

    fn kline(t: u64, o: f64, h: f64, l: f64, c: f64) -> Kline {
        let min_tick = Power10::new(-2); // 0.01
        let vol = Volume::TotalOnly(Qty::from_f64(1.0));
        Kline::new(UnixMs::new(t), o, h, l, c, vol, min_tick)
    }

    #[test]
    fn detects_a_simple_bullish_bos() {
        let klines = vec![
            kline(0, 100.0, 101.0, 99.0, 100.0),
            kline(1, 100.0, 105.0, 99.0, 104.0), // pivot high @105 (t=1)
            kline(2, 104.0, 103.0, 95.0, 96.0),
            kline(3, 96.0, 97.0, 90.0, 91.0),    // pivot low @90 (t=3)
            kline(4, 91.0, 96.0, 90.0, 95.0),    // pivot low @90 (t=4, tie, overwrites)
            kline(5, 95.0, 108.0, 94.0, 107.0),  // close 107 > 105 -> bullish break
            kline(6, 107.0, 109.0, 100.0, 101.0),
        ];

        let mut state = SmcStructureState::new(40);
        state.rebuild(&klines, 1, 1.5);

        assert!(
            state.levels.iter().any(|l| l.direction == Direction::Bullish
                && l.kind == BreakKind::Bos
                && (l.price.to_f64() - 105.0).abs() < 0.01),
            "expected a bullish BOS at 105, got {:?}",
            state.levels
        );
    }

    #[test]
    fn no_break_when_price_stays_inside_range() {
        let klines = vec![
            kline(0, 100.0, 101.0, 99.0, 100.0),
            kline(1, 100.0, 105.0, 99.0, 104.0),
            kline(2, 104.0, 103.0, 95.0, 96.0),
            kline(3, 96.0, 97.0, 90.0, 91.0),
            kline(4, 91.0, 96.0, 90.0, 95.0),
            kline(5, 95.0, 102.0, 94.0, 98.0), // does NOT close above 105
            kline(6, 98.0, 100.0, 93.0, 96.0),
        ];

        let mut state = SmcStructureState::new(40);
        state.rebuild(&klines, 1, 1.5);

        assert!(
            state.levels.is_empty(),
            "expected no breaks, got {:?}",
            state.levels
        );
    }

    #[test]
    fn detects_a_bullish_fvg_gap() {
        // Pad with 20 flat, low-volatility bars so the rolling avg-range
        // window has enough history, then create a clean 3-bar gap at the
        // tail: low[last] > high[last-2] by a wide margin.
        let mut klines: Vec<Kline> = (0..20)
            .map(|i| kline(i, 100.0, 100.5, 99.5, 100.0))
            .collect();
        klines.push(kline(20, 100.0, 100.5, 99.5, 100.2)); // i-2 relative to gap bar
        klines.push(kline(21, 100.2, 100.6, 99.8, 100.3));
        klines.push(kline(22, 108.0, 110.0, 108.0, 109.0)); // low(108) >> high[i-2](100.5)

        let mut state = SmcStructureState::new(40);
        state.rebuild_fvgs(&klines, 0.1);

        assert!(
            state.fvgs.iter().any(|f| f.direction == Direction::Bullish),
            "expected a bullish FVG, got {:?}",
            state.fvgs
        );
    }
}
