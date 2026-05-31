import { useState, useRef, useEffect, useCallback } from "react";

const SYSTEM_PROMPT = `Ты — Hermes, ассистент-трейдер для построения торговых стратегий на USDT-M Futures (Binance Demo).

Пользователь описывает стратегию свободным текстом. Твоя задача — уточнять детали через диалог и в итоге выдать:
1. Чёткое текстовое описание алгоритма по шагам
2. JSON-структуру стратегии

Формат ответа когда алгоритм готов или обновлён:
- Сначала текстовое объяснение/шаги
- Затем JSON строго в теге <strategy_json>...</strategy_json>

Формат JSON:
{
  "name": "Название стратегии",
  "description": "Краткое описание",
  "market": "futures",
  "symbol": "BTCUSDT" (или другой),
  "timeframe": "15m" (или другой),
  "entry": {
    "conditions": ["условие 1", "условие 2"],
    "side": "LONG | SHORT | BOTH",
    "order_type": "MARKET | LIMIT",
    "quantity_usdt": 50
  },
  "exit": {
    "take_profit_percent": 1.5,
    "stop_loss_percent": 0.8,
    "conditions": ["условие выхода"]
  },
  "risk": {
    "max_daily_loss_usdt": 100,
    "max_open_positions": 2,
    "leverage": 10
  },
  "notes": "дополнительные замечания"
}

Если информации недостаточно — задавай уточняющие вопросы по одному. Не выдавай JSON пока стратегия не достаточно определена.
При обновлении стратегии всегда выдавай полный обновлённый JSON.`;

const STORAGE_KEY = "hermes_strategies";

function loadStrategies() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch { return []; }
}

function saveStrategies(list) {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(list)); } catch {}
}

function parseStrategyJson(text) {
  const match = text.match(/<strategy_json>([\s\S]*?)<\/strategy_json>/);
  if (!match) return null;
  try { return JSON.parse(match[1].trim()); } catch { return null; }
}

function stripStrategyTag(text) {
  return text.replace(/<strategy_json>[\s\S]*?<\/strategy_json>/g, "").trim();
}

function AlgorithmPanel({ strategy }) {
  if (!strategy) {
    return (
      <div style={{ display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", height: "100%", gap: 12, opacity: 0.4 }}>
        <svg width="48" height="48" viewBox="0 0 48 48" fill="none">
          <rect x="8" y="8" width="32" height="32" rx="6" stroke="currentColor" strokeWidth="1.5" strokeDasharray="4 3"/>
          <path d="M16 24h16M16 30h10M16 18h8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"/>
        </svg>
        <span style={{ fontSize: 13, fontFamily: "var(--font-mono, monospace)", color: "var(--color-text-secondary)" }}>
          Алгоритм появится здесь
        </span>
      </div>
    );
  }

  const s = strategy;

  const Block = ({ label, children }) => (
    <div style={{ marginBottom: 20 }}>
      <div style={{
        fontSize: 10, letterSpacing: "0.12em", textTransform: "uppercase",
        color: "var(--color-text-tertiary)", fontFamily: "var(--font-mono, monospace)",
        marginBottom: 8, paddingBottom: 6,
        borderBottom: "1px solid var(--color-border-tertiary)"
      }}>{label}</div>
      {children}
    </div>
  );

  const Tag = ({ children, color }) => (
    <span style={{
      display: "inline-block", padding: "2px 8px", borderRadius: 4,
      fontSize: 11, fontFamily: "var(--font-mono, monospace)",
      background: color === "green" ? "rgba(29,158,117,0.12)" : color === "red" ? "rgba(226,75,74,0.12)" : color === "amber" ? "rgba(186,117,23,0.1)" : "var(--color-background-secondary)",
      color: color === "green" ? "#0F6E56" : color === "red" ? "#A32D2D" : color === "amber" ? "#854F0B" : "var(--color-text-secondary)",
      border: `1px solid ${color === "green" ? "rgba(29,158,117,0.2)" : color === "red" ? "rgba(226,75,74,0.2)" : color === "amber" ? "rgba(186,117,23,0.15)" : "var(--color-border-tertiary)"}`,
      marginRight: 4, marginBottom: 4
    }}>{children}</span>
  );

  const Row = ({ k, v, mono }) => (
    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 6 }}>
      <span style={{ fontSize: 12, color: "var(--color-text-secondary)" }}>{k}</span>
      <span style={{
        fontSize: 12, fontFamily: mono ? "var(--font-mono, monospace)" : undefined,
        color: "var(--color-text-primary)", fontWeight: 500
      }}>{v}</span>
    </div>
  );

  return (
    <div style={{ padding: "20px 20px 20px 16px", overflowY: "auto", height: "100%", boxSizing: "border-box" }}>
      <div style={{ marginBottom: 20 }}>
        <div style={{
          fontSize: 16, fontWeight: 600, color: "var(--color-text-primary)",
          marginBottom: 4, lineHeight: 1.3
        }}>{s.name || "Без названия"}</div>
        {s.description && (
          <div style={{ fontSize: 12, color: "var(--color-text-secondary)", lineHeight: 1.5 }}>{s.description}</div>
        )}
      </div>

      <Block label="Инструмент">
        <Row k="Символ" v={s.symbol || "—"} mono />
        <Row k="Таймфрейм" v={s.timeframe || "—"} mono />
        <Row k="Рынок" v={s.market || "—"} />
      </Block>

      {s.entry && (
        <Block label="Вход">
          <Row k="Направление" v={
            s.entry.side === "LONG" ? "🟢 Long" :
            s.entry.side === "SHORT" ? "🔴 Short" : "⚡ Оба"
          } />
          <Row k="Тип ордера" v={s.entry.order_type || "—"} mono />
          <Row k="Объём" v={s.entry.quantity_usdt ? `${s.entry.quantity_usdt} USDT` : "—"} />
          {s.entry.conditions?.length > 0 && (
            <div style={{ marginTop: 8 }}>
              <div style={{ fontSize: 11, color: "var(--color-text-tertiary)", marginBottom: 6 }}>Условия</div>
              {s.entry.conditions.map((c, i) => (
                <div key={i} style={{
                  display: "flex", gap: 8, marginBottom: 5, alignItems: "flex-start"
                }}>
                  <span style={{
                    minWidth: 18, height: 18, borderRadius: "50%",
                    background: "rgba(29,158,117,0.1)", border: "1px solid rgba(29,158,117,0.25)",
                    fontSize: 10, color: "#0F6E56", display: "flex", alignItems: "center",
                    justifyContent: "center", fontWeight: 600, flexShrink: 0, marginTop: 1
                  }}>{i + 1}</span>
                  <span style={{ fontSize: 12, color: "var(--color-text-primary)", lineHeight: 1.5 }}>{c}</span>
                </div>
              ))}
            </div>
          )}
        </Block>
      )}

      {s.exit && (
        <Block label="Выход">
          {s.exit.take_profit_percent && (
            <Row k="Take Profit" v={<Tag color="green">+{s.exit.take_profit_percent}%</Tag>} />
          )}
          {s.exit.stop_loss_percent && (
            <Row k="Stop Loss" v={<Tag color="red">−{s.exit.stop_loss_percent}%</Tag>} />
          )}
          {s.exit.conditions?.map((c, i) => (
            <div key={i} style={{ fontSize: 12, color: "var(--color-text-secondary)", marginBottom: 4, lineHeight: 1.5 }}>· {c}</div>
          ))}
        </Block>
      )}

      {s.risk && (
        <Block label="Риск-менеджмент">
          {s.risk.leverage && <Row k="Плечо" v={<Tag color="amber">×{s.risk.leverage}</Tag>} />}
          {s.risk.max_daily_loss_usdt && <Row k="Макс. убыток/день" v={`${s.risk.max_daily_loss_usdt} USDT`} />}
          {s.risk.max_open_positions && <Row k="Макс. позиций" v={s.risk.max_open_positions} />}
        </Block>
      )}

      {s.notes && (
        <Block label="Заметки">
          <div style={{ fontSize: 12, color: "var(--color-text-secondary)", lineHeight: 1.6, fontStyle: "italic" }}>{s.notes}</div>
        </Block>
      )}

      <Block label="JSON">
        <pre style={{
          fontSize: 10, fontFamily: "var(--font-mono, monospace)",
          background: "var(--color-background-secondary)",
          border: "1px solid var(--color-border-tertiary)",
          borderRadius: 6, padding: 10, margin: 0,
          overflowX: "auto", whiteSpace: "pre-wrap",
          color: "var(--color-text-secondary)", lineHeight: 1.6,
          maxHeight: 280, overflowY: "auto"
        }}>{JSON.stringify(s, null, 2)}</pre>
      </Block>
    </div>
  );
}

function ChatMessage({ msg }) {
  const isUser = msg.role === "user";
  return (
    <div style={{
      display: "flex", flexDirection: isUser ? "row-reverse" : "row",
      gap: 8, marginBottom: 16, alignItems: "flex-start"
    }}>
      {!isUser && (
        <div style={{
          width: 28, height: 28, borderRadius: "50%", flexShrink: 0,
          background: "linear-gradient(135deg, #1D9E75 0%, #0F6E56 100%)",
          display: "flex", alignItems: "center", justifyContent: "center",
          fontSize: 12, color: "#fff", fontWeight: 700, marginTop: 2
        }}>H</div>
      )}
      <div style={{
        maxWidth: "82%",
        background: isUser ? "var(--color-background-info)" : "var(--color-background-secondary)",
        border: `1px solid ${isUser ? "var(--color-border-info)" : "var(--color-border-tertiary)"}`,
        borderRadius: isUser ? "12px 2px 12px 12px" : "2px 12px 12px 12px",
        padding: "10px 14px",
        fontSize: 13, lineHeight: 1.65,
        color: "var(--color-text-primary)"
      }}>
        {msg.content.split("\n").map((line, i) => (
          <span key={i}>{line}{i < msg.content.split("\n").length - 1 && <br />}</span>
        ))}
        {msg.hasJson && (
          <div style={{
            marginTop: 8, padding: "4px 8px", borderRadius: 4,
            background: "rgba(29,158,117,0.08)", border: "1px solid rgba(29,158,117,0.2)",
            fontSize: 11, color: "#0F6E56", display: "flex", alignItems: "center", gap: 5
          }}>
            <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
              <path d="M2 6l3 3 5-5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
            Алгоритм обновлён
          </div>
        )}
      </div>
    </div>
  );
}

export default function StrategyEditor() {
  const [strategies, setStrategies] = useState(() => loadStrategies());
  const [activeId, setActiveId] = useState(null);
  const [messages, setMessages] = useState([]);
  const [currentStrategy, setCurrentStrategy] = useState(null);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [showSidebar, setShowSidebar] = useState(true);
  const [newName, setNewName] = useState("");
  const [showNewModal, setShowNewModal] = useState(false);
  const bottomRef = useRef(null);
  const textareaRef = useRef(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const openStrategy = useCallback((s) => {
    setActiveId(s.id);
    setMessages(s.messages || []);
    setCurrentStrategy(s.strategy || null);
  }, []);

  const createStrategy = useCallback(() => {
    const name = newName.trim() || "Новая стратегия";
    const s = { id: Date.now().toString(), name, messages: [], strategy: null, createdAt: Date.now() };
    const updated = [s, ...strategies];
    setStrategies(updated);
    saveStrategies(updated);
    setActiveId(s.id);
    setMessages([]);
    setCurrentStrategy(null);
    setNewName("");
    setShowNewModal(false);
  }, [newName, strategies]);

  const deleteStrategy = useCallback((id, e) => {
    e.stopPropagation();
    const updated = strategies.filter(s => s.id !== id);
    setStrategies(updated);
    saveStrategies(updated);
    if (activeId === id) {
      setActiveId(null);
      setMessages([]);
      setCurrentStrategy(null);
    }
  }, [strategies, activeId]);

  const persistCurrent = useCallback((msgs, strat, id) => {
    setStrategies(prev => {
      const updated = prev.map(s => s.id === id ? {
        ...s,
        messages: msgs,
        strategy: strat,
        name: strat?.name || s.name
      } : s);
      saveStrategies(updated);
      return updated;
    });
  }, []);

  const sendMessage = useCallback(async () => {
    if (!input.trim() || loading || !activeId) return;
    const userMsg = { role: "user", content: input.trim() };
    const nextMsgs = [...messages, userMsg];
    setMessages(nextMsgs);
    setInput("");
    setLoading(true);

    try {
      const apiMessages = nextMsgs.map(m => ({ role: m.role, content: m.content }));
      const res = await fetch("https://api.anthropic.com/v1/messages", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          model: "claude-sonnet-4-20250514",
          max_tokens: 1000,
          system: SYSTEM_PROMPT,
          messages: apiMessages
        })
      });
      const data = await res.json();
      const raw = data.content?.map(b => b.text || "").join("") || "Ошибка ответа";
      const strat = parseStrategyJson(raw);
      const clean = stripStrategyTag(raw);
      const assistantMsg = { role: "assistant", content: clean, hasJson: !!strat };
      const finalMsgs = [...nextMsgs, assistantMsg];
      const finalStrat = strat || currentStrategy;
      setMessages(finalMsgs);
      if (strat) setCurrentStrategy(strat);
      persistCurrent(finalMsgs, finalStrat, activeId);
    } catch (err) {
      const errMsg = { role: "assistant", content: "Ошибка соединения. Попробуйте ещё раз." };
      const finalMsgs = [...nextMsgs, errMsg];
      setMessages(finalMsgs);
      persistCurrent(finalMsgs, currentStrategy, activeId);
    }
    setLoading(false);
  }, [input, loading, activeId, messages, currentStrategy, persistCurrent]);

  const handleKey = (e) => {
    if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); sendMessage(); }
  };

  const active = strategies.find(s => s.id === activeId);

  return (
    <div style={{
      display: "flex", height: "100vh", overflow: "hidden",
      fontFamily: "'DM Sans', var(--font-sans), sans-serif",
      background: "var(--color-background-primary)",
      color: "var(--color-text-primary)"
    }}>
      {/* Sidebar */}
      {showSidebar && (
        <div style={{
          width: 220, flexShrink: 0, borderRight: "1px solid var(--color-border-tertiary)",
          display: "flex", flexDirection: "column", overflow: "hidden"
        }}>
          <div style={{
            padding: "14px 14px 10px",
            borderBottom: "1px solid var(--color-border-tertiary)",
            display: "flex", alignItems: "center", justifyContent: "space-between"
          }}>
            <span style={{ fontSize: 12, fontWeight: 600, letterSpacing: "0.06em", textTransform: "uppercase", color: "var(--color-text-secondary)" }}>
              Стратегии
            </span>
            <button onClick={() => setShowNewModal(true)} style={{
              width: 26, height: 26, borderRadius: 6, border: "1px solid var(--color-border-secondary)",
              background: "transparent", cursor: "pointer", color: "var(--color-text-primary)",
              display: "flex", alignItems: "center", justifyContent: "center", fontSize: 16
            }}>+</button>
          </div>
          <div style={{ overflowY: "auto", flex: 1, padding: "8px 8px" }}>
            {strategies.length === 0 && (
              <div style={{ fontSize: 12, color: "var(--color-text-tertiary)", textAlign: "center", marginTop: 24, lineHeight: 1.6 }}>
                Нет стратегий.<br/>Создайте первую.
              </div>
            )}
            {strategies.map(s => (
              <div key={s.id} onClick={() => openStrategy(s)} style={{
                padding: "8px 10px", borderRadius: 7, marginBottom: 2, cursor: "pointer",
                background: s.id === activeId ? "var(--color-background-secondary)" : "transparent",
                border: `1px solid ${s.id === activeId ? "var(--color-border-secondary)" : "transparent"}`,
                display: "flex", alignItems: "center", justifyContent: "space-between",
                transition: "background 0.15s"
              }}>
                <div style={{ overflow: "hidden" }}>
                  <div style={{ fontSize: 12, fontWeight: 500, color: "var(--color-text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
                    {s.strategy?.name || s.name}
                  </div>
                  <div style={{ fontSize: 10, color: "var(--color-text-tertiary)", marginTop: 2 }}>
                    {s.strategy?.symbol ? `${s.strategy.symbol} · ` : ""}{s.messages?.length || 0} сообщ.
                  </div>
                </div>
                <button onClick={(e) => deleteStrategy(s.id, e)} style={{
                  background: "transparent", border: "none", cursor: "pointer",
                  color: "var(--color-text-tertiary)", fontSize: 14, padding: "2px 4px",
                  borderRadius: 4, flexShrink: 0, lineHeight: 1
                }}>×</button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Main area */}
      <div style={{ flex: 1, display: "flex", flexDirection: "column", overflow: "hidden", minWidth: 0 }}>
        {/* Header */}
        <div style={{
          height: 48, borderBottom: "1px solid var(--color-border-tertiary)",
          display: "flex", alignItems: "center", padding: "0 16px", gap: 10, flexShrink: 0
        }}>
          <button onClick={() => setShowSidebar(v => !v)} style={{
            background: "transparent", border: "none", cursor: "pointer",
            color: "var(--color-text-secondary)", padding: 4, borderRadius: 4, lineHeight: 0
          }}>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
              <path d="M2 4h12M2 8h12M2 12h12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"/>
            </svg>
          </button>
          <div style={{
            width: 8, height: 8, borderRadius: "50%",
            background: activeId ? "#1D9E75" : "var(--color-border-secondary)"
          }} />
          <span style={{ fontSize: 13, fontWeight: 500, color: "var(--color-text-primary)" }}>
            {active?.strategy?.name || active?.name || "Выберите или создайте стратегию"}
          </span>
          {active?.strategy?.symbol && (
            <span style={{
              fontSize: 11, padding: "2px 7px", borderRadius: 4,
              background: "rgba(29,158,117,0.1)", color: "#0F6E56",
              border: "1px solid rgba(29,158,117,0.2)", fontFamily: "monospace"
            }}>{active.strategy.symbol}</span>
          )}
        </div>

        {/* Body */}
        <div style={{ flex: 1, display: "flex", overflow: "hidden", minHeight: 0 }}>
          {/* Chat */}
          <div style={{ flex: 1, display: "flex", flexDirection: "column", overflow: "hidden", minWidth: 0 }}>
            {!activeId ? (
              <div style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 16, padding: 32, opacity: 0.5 }}>
                <svg width="56" height="56" viewBox="0 0 56 56" fill="none">
                  <rect x="8" y="8" width="40" height="40" rx="10" stroke="currentColor" strokeWidth="1.5"/>
                  <path d="M18 22h20M18 28h14M18 34h8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round"/>
                </svg>
                <div style={{ textAlign: "center" }}>
                  <div style={{ fontSize: 14, fontWeight: 500, marginBottom: 6 }}>Нет активной стратегии</div>
                  <div style={{ fontSize: 12, color: "var(--color-text-secondary)" }}>
                    Выберите из списка или создайте новую
                  </div>
                </div>
                <button onClick={() => setShowNewModal(true)} style={{
                  padding: "8px 20px", borderRadius: 8, border: "1px solid var(--color-border-secondary)",
                  background: "var(--color-background-secondary)", cursor: "pointer",
                  fontSize: 13, color: "var(--color-text-primary)", fontWeight: 500
                }}>+ Новая стратегия</button>
              </div>
            ) : (
              <>
                <div style={{ flex: 1, overflowY: "auto", padding: "20px 20px 8px" }}>
                  {messages.length === 0 && (
                    <div style={{ textAlign: "center", padding: "32px 16px", opacity: 0.45 }}>
                      <div style={{ fontSize: 13, color: "var(--color-text-secondary)", lineHeight: 1.7 }}>
                        Опишите стратегию своими словами.<br/>
                        Например: «Торгую BTC лонг на 15м, вход при пробое уровня сопротивления,<br/>
                        стоп 0.8%, тейк 2%, максимум 2 позиции одновременно»
                      </div>
                    </div>
                  )}
                  {messages.map((m, i) => <ChatMessage key={i} msg={m} />)}
                  {loading && (
                    <div style={{ display: "flex", gap: 8, alignItems: "center", marginBottom: 16 }}>
                      <div style={{
                        width: 28, height: 28, borderRadius: "50%",
                        background: "linear-gradient(135deg, #1D9E75 0%, #0F6E56 100%)",
                        display: "flex", alignItems: "center", justifyContent: "center",
                        fontSize: 12, color: "#fff", fontWeight: 700
                      }}>H</div>
                      <div style={{
                        padding: "10px 14px", borderRadius: "2px 12px 12px 12px",
                        background: "var(--color-background-secondary)",
                        border: "1px solid var(--color-border-tertiary)",
                        display: "flex", gap: 4, alignItems: "center"
                      }}>
                        {[0, 0.18, 0.36].map((d, i) => (
                          <div key={i} style={{
                            width: 6, height: 6, borderRadius: "50%",
                            background: "var(--color-text-tertiary)",
                            animation: "pulse 1.2s ease-in-out infinite",
                            animationDelay: `${d}s`
                          }} />
                        ))}
                      </div>
                    </div>
                  )}
                  <div ref={bottomRef} />
                </div>
                <div style={{ padding: "12px 16px 16px", borderTop: "1px solid var(--color-border-tertiary)" }}>
                  <div style={{ display: "flex", gap: 8, alignItems: "flex-end" }}>
                    <textarea
                      ref={textareaRef}
                      value={input}
                      onChange={e => setInput(e.target.value)}
                      onKeyDown={handleKey}
                      placeholder="Опишите стратегию или уточните детали…"
                      rows={2}
                      style={{
                        flex: 1, resize: "none", padding: "10px 14px",
                        borderRadius: 10, border: "1px solid var(--color-border-secondary)",
                        background: "var(--color-background-secondary)",
                        color: "var(--color-text-primary)", fontSize: 13, lineHeight: 1.5,
                        outline: "none", fontFamily: "inherit"
                      }}
                    />
                    <button onClick={sendMessage} disabled={!input.trim() || loading} style={{
                      width: 40, height: 40, borderRadius: 10, flexShrink: 0,
                      background: input.trim() && !loading ? "#1D9E75" : "var(--color-background-secondary)",
                      border: "1px solid " + (input.trim() && !loading ? "#1D9E75" : "var(--color-border-secondary)"),
                      cursor: input.trim() && !loading ? "pointer" : "not-allowed",
                      color: input.trim() && !loading ? "#fff" : "var(--color-text-tertiary)",
                      display: "flex", alignItems: "center", justifyContent: "center",
                      transition: "all 0.15s"
                    }}>
                      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
                        <path d="M2 14L14 8 2 2v4.5l8 1.5-8 1.5V14z" fill="currentColor"/>
                      </svg>
                    </button>
                  </div>
                  <div style={{ fontSize: 10, color: "var(--color-text-tertiary)", marginTop: 6, paddingLeft: 2 }}>
                    Enter — отправить · Shift+Enter — новая строка
                  </div>
                </div>
              </>
            )}
          </div>

          {/* Algorithm panel */}
          <div style={{
            width: 280, flexShrink: 0,
            borderLeft: "1px solid var(--color-border-tertiary)",
            display: "flex", flexDirection: "column", overflow: "hidden"
          }}>
            <div style={{
              height: 40, borderBottom: "1px solid var(--color-border-tertiary)",
              display: "flex", alignItems: "center", padding: "0 16px",
              fontSize: 11, fontWeight: 600, letterSpacing: "0.08em",
              textTransform: "uppercase", color: "var(--color-text-tertiary)", flexShrink: 0
            }}>
              Алгоритм
            </div>
            <div style={{ flex: 1, overflow: "hidden" }}>
              <AlgorithmPanel strategy={currentStrategy} />
            </div>
          </div>
        </div>
      </div>

      {/* New strategy modal */}
      {showNewModal && (
        <div onClick={() => setShowNewModal(false)} style={{
          position: "fixed", inset: 0, background: "rgba(0,0,0,0.4)",
          display: "flex", alignItems: "center", justifyContent: "center", zIndex: 100
        }}>
          <div onClick={e => e.stopPropagation()} style={{
            background: "var(--color-background-primary)",
            border: "1px solid var(--color-border-secondary)",
            borderRadius: 12, padding: 24, width: 320,
            boxShadow: "0 8px 32px rgba(0,0,0,0.15)"
          }}>
            <div style={{ fontSize: 15, fontWeight: 600, marginBottom: 16 }}>Новая стратегия</div>
            <input
              value={newName}
              onChange={e => setNewName(e.target.value)}
              onKeyDown={e => e.key === "Enter" && createStrategy()}
              placeholder="Название (необязательно)"
              autoFocus
              style={{
                width: "100%", padding: "9px 12px", borderRadius: 8,
                border: "1px solid var(--color-border-secondary)",
                background: "var(--color-background-secondary)",
                color: "var(--color-text-primary)", fontSize: 13,
                outline: "none", boxSizing: "border-box", marginBottom: 14
              }}
            />
            <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
              <button onClick={() => setShowNewModal(false)} style={{
                padding: "7px 16px", borderRadius: 7, border: "1px solid var(--color-border-secondary)",
                background: "transparent", cursor: "pointer", fontSize: 13, color: "var(--color-text-secondary)"
              }}>Отмена</button>
              <button onClick={createStrategy} style={{
                padding: "7px 16px", borderRadius: 7, border: "none",
                background: "#1D9E75", cursor: "pointer", fontSize: 13,
                color: "#fff", fontWeight: 500
              }}>Создать</button>
            </div>
          </div>
        </div>
      )}

      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 0.3; transform: scale(0.85); }
          50% { opacity: 1; transform: scale(1); }
        }
        textarea::placeholder { color: var(--color-text-tertiary); }
        input::placeholder { color: var(--color-text-tertiary); }
        ::-webkit-scrollbar { width: 4px; }
        ::-webkit-scrollbar-track { background: transparent; }
        ::-webkit-scrollbar-thumb { background: var(--color-border-secondary); border-radius: 2px; }
      `}</style>
    </div>
  );
}
