namespace ReentrySync.Web
{
    public static class DashboardHtml
    {
        public static string GetHtml(int httpPort, int wsPort)
        {
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Reentry Mission Control & Copilot</title>
    <style>
        :root {{
            --bg: #0c0f12;
            --panel-bg: #141920;
            --panel-border: #222b38;
            --green: #2ecc71;
            --dsky-green: #33ff66;
            --amber: #f39c12;
            --blue: #3498db;
            --red: #e74c3c;
            --text-main: #e0e6ed;
            --text-dim: #7f8c9d;
            --font-mono: 'Consolas', 'Courier New', monospace;
        }}

        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            background-color: var(--bg);
            color: var(--text-main);
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
        }}

        header {{
            background-color: var(--panel-bg);
            border-bottom: 2px solid var(--panel-border);
            padding: 12px 24px;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }}

        .header-title {{
            display: flex;
            align-items: center;
            gap: 12px;
            font-size: 1.25rem;
            font-weight: 700;
            letter-spacing: 1px;
            color: #fff;
        }}

        .status-badge {{
            display: inline-flex;
            align-items: center;
            gap: 8px;
            padding: 4px 12px;
            border-radius: 999px;
            font-size: 0.8rem;
            font-weight: 600;
            text-transform: uppercase;
            background: #1a222d;
            border: 1px solid var(--panel-border);
        }}

        .status-dot {{
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: var(--red);
        }}
        .status-dot.connected {{
            background: var(--green);
            box-shadow: 0 0 8px var(--green);
        }}

        nav {{
            display: flex;
            gap: 4px;
            background: #11161d;
            padding: 8px 24px 0 24px;
            border-bottom: 1px solid var(--panel-border);
        }}

        .nav-btn {{
            background: none;
            border: none;
            color: var(--text-dim);
            padding: 10px 20px;
            font-size: 0.95rem;
            font-weight: 600;
            cursor: pointer;
            border-bottom: 3px solid transparent;
            transition: all 0.2s;
        }}
        .nav-btn:hover {{ color: #fff; }}
        .nav-btn.active {{
            color: var(--dsky-green);
            border-bottom-color: var(--dsky-green);
        }}

        main {{
            flex: 1;
            padding: 24px;
            max-width: 1400px;
            margin: 0 auto;
            width: 100%;
        }}

        .tab-content {{ display: none; }}
        .tab-content.active {{ display: block; }}

        /* Grid Cards */
        .card-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
            gap: 20px;
        }}

        .card {{
            background: var(--panel-bg);
            border: 1px solid var(--panel-border);
            border-radius: 8px;
            padding: 18px;
        }}

        .card-header {{
            font-size: 0.85rem;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1px;
            color: var(--text-dim);
            margin-bottom: 14px;
            display: flex;
            justify-content: space-between;
        }}

        .metric-row {{
            display: flex;
            justify-content: space-between;
            padding: 8px 0;
            border-bottom: 1px solid #1c2430;
            font-family: var(--font-mono);
        }}
        .metric-label {{ color: var(--text-dim); }}
        .metric-value {{ font-weight: 700; color: #fff; }}

        /* DSKY Styling */
        .dsky-container {{
            display: flex;
            justify-content: center;
            gap: 32px;
            flex-wrap: wrap;
            padding: 20px 0;
        }}

        .dsky-unit {{
            background: #1e252e;
            border: 4px solid #323d4b;
            border-radius: 12px;
            padding: 24px;
            width: 480px;
            box-shadow: 0 16px 32px rgba(0,0,0,0.5);
        }}

        .dsky-display {{
            background: #080d08;
            border: 2px solid #192419;
            border-radius: 6px;
            padding: 16px;
            font-family: var(--font-mono);
            color: var(--dsky-green);
            text-shadow: 0 0 10px rgba(51,255,102,0.6);
            margin-bottom: 20px;
        }}

        .dsky-row {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 8px;
        }}

        .dsky-digits {{
            font-size: 2.2rem;
            letter-spacing: 4px;
            font-weight: 700;
        }}

        .dsky-label {{
            font-size: 0.8rem;
            color: #558855;
            text-transform: uppercase;
        }}

        .dsky-keypad {{
            display: grid;
            grid-template-columns: repeat(4, 1fr);
            gap: 10px;
        }}

        .dsky-btn {{
            background: #2a3441;
            color: #e0e6ed;
            border: 1px solid #3d4a5d;
            border-radius: 6px;
            padding: 16px;
            font-size: 1.1rem;
            font-weight: 700;
            cursor: pointer;
            box-shadow: 0 4px 0 #1b232c;
            transition: all 0.1s;
            font-family: var(--font-mono);
        }}
        .dsky-btn:active {{
            transform: translateY(3px);
            box-shadow: 0 1px 0 #1b232c;
        }}
        .dsky-btn.action {{
            background: #1b4b72;
            border-color: #24659b;
        }}

        /* Switchboard */
        .switch-search {{
            width: 100%;
            padding: 12px 16px;
            background: var(--panel-bg);
            border: 1px solid var(--panel-border);
            border-radius: 6px;
            color: #fff;
            font-size: 1rem;
            margin-bottom: 16px;
        }}

        .switch-table {{
            width: 100%;
            border-collapse: collapse;
            font-family: var(--font-mono);
        }}
        .switch-table th, .switch-table td {{
            padding: 12px;
            text-align: left;
            border-bottom: 1px solid var(--panel-border);
        }}
        .switch-table th {{ color: var(--text-dim); }}

        .btn-group {{ display: flex; gap: 6px; }}
        .sw-btn {{
            background: #222b38;
            border: 1px solid #344356;
            color: #fff;
            padding: 4px 10px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 0.8rem;
            font-weight: 600;
        }}
        .sw-btn.active {{
            background: var(--green);
            color: #000;
            border-color: var(--green);
        }}

        /* Checklist */
        .checklist-box {{
            background: var(--panel-bg);
            border: 1px solid var(--panel-border);
            border-radius: 8px;
            padding: 24px;
        }}

        .checklist-header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 20px;
            flex-wrap: wrap;
            gap: 16px;
        }}

        .checklist-selector-bar {{
            display: flex;
            gap: 10px;
            margin-bottom: 20px;
            padding-bottom: 16px;
            border-bottom: 1px solid var(--panel-border);
        }}

        .chk-select {{
            flex: 1;
            padding: 10px 14px;
            background: #1a222d;
            border: 1px solid var(--panel-border);
            border-radius: 6px;
            color: #fff;
            font-size: 0.95rem;
        }}

        .btn-auto {{
            background: var(--green);
            color: #000;
            border: none;
            padding: 10px 20px;
            font-weight: 700;
            font-size: 1rem;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s;
        }}
        .btn-auto:hover {{ filter: brightness(1.1); }}

        .current-step-banner {{
            background: #11281a;
            border: 2px solid var(--green);
            border-radius: 8px;
            padding: 16px;
            margin-bottom: 20px;
            box-shadow: 0 0 16px rgba(46, 204, 113, 0.2);
        }}
        .current-step-banner .title {{
            font-size: 0.8rem;
            color: var(--green);
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 1px;
            margin-bottom: 6px;
        }}
        .current-step-banner .instruction {{
            font-size: 1.15rem;
            font-weight: 600;
            color: #fff;
        }}

        .step-item {{
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px;
            border-bottom: 1px solid #1a2330;
        }}
        .step-item.current {{
            background: rgba(46, 204, 113, 0.08);
            border-left: 3px solid var(--green);
        }}
        .step-item.ok {{
            opacity: 0.75;
        }}
        .step-badge {{
            background: #202b38;
            color: var(--blue);
            padding: 2px 8px;
            border-radius: 4px;
            font-size: 0.75rem;
            font-weight: 700;
            font-family: var(--font-mono);
        }}
        .step-text {{
            flex: 1;
            font-size: 0.95rem;
        }}

        .copilot-banner {{
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 12px;
            background: #101924;
            border: 1px solid #26384d;
            border-radius: 6px;
            padding: 10px 14px;
            margin-bottom: 16px;
            font-family: var(--font-mono);
            font-size: 0.88rem;
            color: #c9d8e8;
        }}
        .copilot-banner.running {{
            border-color: var(--blue);
            background: #0f2033;
            color: #fff;
        }}
        .copilot-banner.holding {{
            border-color: #f1c40f;
            background: #2b230d;
            color: #f9e79f;
        }}
        .speed-group {{
            display: flex;
            gap: 6px;
            align-items: center;
        }}
        .speed-btn {{
            background: #1b2430;
            color: var(--text-dim);
            border: 1px solid #2b3847;
            padding: 4px 10px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 0.78rem;
            font-weight: 600;
        }}
        .speed-btn.active {{
            background: var(--blue);
            color: #fff;
            border-color: var(--blue);
        }}
        .btn-danger {{
            background: #3a1d24;
            color: #ff6b7a;
            border: 1px solid #5e2a35;
            padding: 10px 14px;
            font-weight: 700;
            font-size: 0.9rem;
            border-radius: 6px;
            cursor: pointer;
        }}
        .btn-danger:hover {{ background: #52242e; }}

        /* Mission Director, Autopilot & Burn Planner Styles */
        .hdr-meta {{
            display: flex;
            gap: 10px;
            align-items: center;
            font-family: var(--font-mono);
            font-size: 0.85rem;
        }}
        .pill {{
            background: #16202c;
            border: 1px solid #2b3c4f;
            padding: 4px 10px;
            border-radius: 6px;
            color: #d6e4f2;
            font-weight: 700;
        }}
        .pill.met {{ color: var(--dsky-green); border-color: #1e4228; background: #0c1c12; }}
        .pill.phase {{ color: #f1c40f; border-color: #544514; }}
        .director-card {{
            background: #0f1924;
            border: 1px solid #28415c;
            border-radius: 8px;
            padding: 16px;
            margin-bottom: 18px;
        }}
        .director-top {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 10px;
            margin-bottom: 12px;
        }}
        .timeline-list {{
            display: flex;
            flex-direction: column;
            gap: 6px;
            max-height: 180px;
            overflow-y: auto;
            margin-top: 10px;
        }}
        .timeline-row {{
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 7px 10px;
            background: #131c27;
            border-radius: 5px;
            font-size: 0.86rem;
            border-left: 3px solid #2c3b4c;
        }}
        .timeline-row.due {{ border-left-color: #f1c40f; background: #1e2418; }}
        .timeline-row.done {{ border-left-color: var(--green); opacity: 0.6; }}

        .ap-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 10px;
            margin-top: 10px;
        }}
        .ap-btn {{
            background: #1a2431;
            color: #e2edf8;
            border: 1px solid #2f4054;
            border-radius: 6px;
            padding: 12px 10px;
            font-weight: 700;
            font-size: 0.88rem;
            cursor: pointer;
            transition: all 0.15s;
        }}
        .ap-btn:hover {{ background: #243347; border-color: var(--blue); }}
        .ap-btn.active {{ background: var(--blue); color: #fff; border-color: #66b2ff; box-shadow: 0 0 12px rgba(52,152,219,0.4); }}
        .ap-btn.flight-mode {{ padding: 15px 12px; font-size: 0.95rem; text-align: left; }}
        .ap-btn.flight-mode.active {{ background: #27ae60; border-color: #58d68d; box-shadow: 0 0 14px rgba(46,204,113,0.45); }}

        .form-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 12px;
            margin-bottom: 14px;
        }}
        .form-field label {{
            display: block;
            font-size: 0.75rem;
            color: var(--text-dim);
            text-transform: uppercase;
            margin-bottom: 4px;
            font-weight: 700;
        }}
        .form-field input, .form-field select {{
            width: 100%;
            padding: 9px 10px;
            background: #161e28;
            border: 1px solid #2b394a;
            border-radius: 5px;
            color: #fff;
            font-family: var(--font-mono);
            font-size: 0.9rem;
        }}
        .moon-canvas {{
            width: 100%;
            height: 280px;
            background: #090d12;
            border: 1px solid #2b3a4c;
            border-radius: 8px;
            cursor: crosshair;
            display: block;
        }}
        .pad-pre {{
            background: #0a1016;
            border: 1px solid #1e2d3d;
            border-radius: 6px;
            padding: 10px;
            font-family: var(--font-mono);
            font-size: 0.78rem;
            color: var(--dsky-green);
            white-space: pre-wrap;
            max-height: 150px;
            overflow-y: auto;
            margin-top: 10px;
        }}
    </style>
</head>
<body>
    <header>
        <div class=""header-title"">
            <span>REENTRY MISSION CONTROL</span>
            <span style=""font-size:0.8rem; color:var(--text-dim);"">AUTONOMOUS GNC &amp; COPILOT</span>
        </div>
        <div class=""hdr-meta"">
            <span class=""pill met"" id=""hdr-met"">MET 000:00:00</span>
            <span class=""pill phase"" id=""hdr-phase"">STANDBY</span>
            <span class=""pill"" id=""hdr-soi"">EARTH</span>
            <span class=""pill"" id=""hdr-mp"" style=""color:#a9cce3;"">SOLO</span>
            <div class=""status-badge"">
                <span class=""status-dot"" id=""statusDot""></span>
                <span id=""statusText"">DISCONNECTED</span>
            </div>
        </div>
    </header>

    <nav>
        <button class=""nav-btn active"" onclick=""switchTab('telemetry', this)"">TELEMETRY</button>
        <button class=""nav-btn"" onclick=""switchTab('dsky', this)"">APOLLO DSKY</button>
        <button class=""nav-btn"" onclick=""switchTab('switchboard', this)"">SWITCHBOARD</button>
        <button class=""nav-btn"" onclick=""switchTab('checklist', this)"">CHECKLIST &amp; DIRECTOR</button>
        <button class=""nav-btn"" onclick=""switchTab('autopilot', this)"">AUTOPILOT &amp; FLIGHT</button>
        <button class=""nav-btn"" onclick=""switchTab('planner', this)"">ORBIT &amp; LUNAR LANDING</button>
        <button class=""nav-btn"" onclick=""switchTab('multiplayer', this)"">CREW CO-OP</button>
    </nav>

    <main>
        <!-- TELEMETRY TAB -->
        <div id=""tab-telemetry"" class=""tab-content active"">
            <div class=""card-grid"">
                <div class=""card"">
                    <div class=""card-header"">Orbital Mechanics</div>
                    <div class=""metric-row""><span class=""metric-label"">Apoapsis (Ap)</span><span class=""metric-value"" id=""telem-ap"">-- NM</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Periapsis (Pe)</span><span class=""metric-value"" id=""telem-pe"">-- NM</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Inertial Velocity</span><span class=""metric-value"" id=""telem-vel"">-- ft/s</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Inclination</span><span class=""metric-value"" id=""telem-inc"">-- °</span></div>
                </div>

                <div class=""card"">
                    <div class=""card-header"">Electrical &amp; Power</div>
                    <div class=""metric-row""><span class=""metric-label"">Main Bus A</span><span class=""metric-value"" id=""telem-busa"">-- V</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Main Bus B</span><span class=""metric-value"" id=""telem-busb"">-- V</span></div>
                </div>

                <div class=""card"">
                    <div class=""card-header"">Flight Status &amp; Environment</div>
                    <div class=""metric-row""><span class=""metric-label"">Active Craft</span><span class=""metric-value"" id=""telem-craft"">Awaiting Craft</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Altitude</span><span class=""metric-value"" id=""telem-alt"">-- ft</span></div>
                </div>
            </div>
        </div>

        <!-- DSKY TAB -->
        <div id=""tab-dsky"" class=""tab-content"">
            <div class=""dsky-container"">
                <div class=""dsky-unit"">
                    <div class=""dsky-display"">
                        <div class=""dsky-row"">
                            <div><span class=""dsky-label"">PROG</span><div class=""dsky-digits"" id=""dsky-prog"">00</div></div>
                            <div><span class=""dsky-label"">VERB</span><div class=""dsky-digits"" id=""dsky-verb"">00</div></div>
                            <div><span class=""dsky-label"">NOUN</span><div class=""dsky-digits"" id=""dsky-noun"">00</div></div>
                        </div>
                        <hr style=""border-color:#1c2d1c; margin:10px 0;"">
                        <div class=""dsky-row""><span class=""dsky-label"">R1</span><div class=""dsky-digits"" id=""dsky-r1"">+00000</div></div>
                        <div class=""dsky-row""><span class=""dsky-label"">R2</span><div class=""dsky-digits"" id=""dsky-r2"">+00000</div></div>
                        <div class=""dsky-row""><span class=""dsky-label"">R3</span><div class=""dsky-digits"" id=""dsky-r3"">+00000</div></div>
                    </div>

                    <div class=""dsky-keypad"">
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('VERB')"">VERB</button>
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('NOUN')"">NOUN</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('+')"">+</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('-')"">-</button>

                        <button class=""dsky-btn"" onclick=""sendDskyKey('7')"">7</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('8')"">8</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('9')"">9</button>
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('CLR')"">CLR</button>

                        <button class=""dsky-btn"" onclick=""sendDskyKey('4')"">4</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('5')"">5</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('6')"">6</button>
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('PRO')"">PRO</button>

                        <button class=""dsky-btn"" onclick=""sendDskyKey('1')"">1</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('2')"">2</button>
                        <button class=""dsky-btn"" onclick=""sendDskyKey('3')"">3</button>
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('KEYREL')"">KEY</button>

                        <button class=""dsky-btn"" onclick=""sendDskyKey('0')"">0</button>
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('ENTR')"">ENTR</button>
                        <button class=""dsky-btn action"" onclick=""sendDskyKey('RSET')"">RSET</button>
                    </div>
                </div>
            </div>
        </div>

        <!-- SWITCHBOARD TAB -->
        <div id=""tab-switchboard"" class=""tab-content"">
            <input type=""text"" id=""switchSearch"" class=""switch-search"" placeholder=""Search 300+ Apollo cockpit switches (e.g. FC1, Inverter, SPS, Cryo)..."" oninput=""filterSwitches()"">
            <div style=""max-height: 600px; overflow-y: auto; background: var(--panel-bg); border-radius: 8px;"">
                <table class=""switch-table"">
                    <thead>
                        <tr>
                            <th>SWITCH ID</th>
                            <th>CURRENT STATE</th>
                            <th>COMMAND ACTUATION</th>
                        </tr>
                    </thead>
                    <tbody id=""switchTableBody"">
                        <tr><td colspan=""3"" style=""text-align:center; color:var(--text-dim);"">Connecting to spacecraft switchboard...</td></tr>
                    </tbody>
                </table>
            </div>
        </div>

        <!-- CHECKLIST & DIRECTOR TAB -->
        <div id=""tab-checklist"" class=""tab-content"">
            <!-- MET & Flight-Phase Autonomous Mission Director -->
            <div class=""director-card"">
                <div class=""director-top"">
                    <div>
                        <div style=""font-size:0.75rem; color:var(--blue); font-weight:700; letter-spacing:1px;"">MET-AWARE AUTONOMOUS MISSION DIRECTOR</div>
                        <div style=""font-size:1.05rem; font-weight:700; margin-top:4px;"" id=""dir-rec-name"">Analyzing Mission Phase &amp; MET...</div>
                        <div style=""font-size:0.85rem; color:var(--text-dim); margin-top:2px;"" id=""dir-rec-reason"">Monitoring flight clock and telemetry...</div>
                    </div>
                    <div style=""display:flex; gap:8px; flex-wrap:wrap;"">
                        <button class=""btn-auto"" style=""background:#2980b9; color:#fff; font-size:0.88rem;"" id=""btnDirectorAuto"" onclick=""toggleAutoDirector()"">AUTO-DIRECTOR: OFF</button>
                        <button class=""btn-auto"" style=""font-size:0.88rem;"" onclick=""runRecommendedChecklist()"">RUN RECOMMENDED</button>
                        <button class=""speed-btn"" onclick=""resetDirectorTimeline()"">RESET TIMELINE</button>
                    </div>
                </div>
                <div style=""font-family:var(--font-mono); font-size:0.8rem; color:#8fb2d4;"" id=""dir-last-action"">Standing by for MET triggers...</div>
                <div class=""timeline-list"" id=""dirTimelineList""></div>
            </div>

            <div class=""checklist-box"">
                <!-- Checklist Picker -->
                <div class=""checklist-selector-bar"">
                    <select id=""checklistPicker"" class=""chk-select"">
                        <option value="""">-- Select a Flight Procedure / Checklist to Load --</option>
                    </select>
                    <button class=""btn-auto"" style=""background:var(--blue); color:#fff;"" onclick=""loadSelectedChecklist()"">LOAD IN COCKPIT</button>
                </div>

                <div class=""checklist-header"">
                    <div>
                        <h2 id=""chk-title"">No Active Checklist</h2>
                        <p id=""chk-subtitle"" style=""color:var(--text-dim); font-size:0.9rem;"">Select a procedure above or open one in the spacecraft.</p>
                    </div>
                    <div style=""display:flex; gap:10px; flex-wrap:wrap;"">
                        <button class=""btn-auto"" onclick=""executeNextChecklistStep()"">NEXT STEP</button>
                        <button class=""btn-auto"" style=""background:var(--blue); color:#fff;"" onclick=""toggleAutoRunner()"" id=""btnAutoToggle"">AUTO-COPILOT</button>
                        <button class=""btn-danger"" onclick=""clearChecklist()"">CLEAR</button>
                    </div>
                </div>

                <!-- Live Copilot Activity & Speed Bar -->
                <div id=""copilotBanner"" class=""copilot-banner"">
                    <span id=""copilotStatusText"" style=""flex:1;"">Auto-Copilot standing by.</span>
                    <div class=""speed-group"">
                        <button class=""speed-btn"" id=""btnSkipHold"" style=""display:none; background:#d4ac0d; color:#000; border-color:#f4d03f;"" onclick=""skipCopilotHold()"">SKIP HOLD</button>
                        <button class=""speed-btn active"" id=""btnRespectTs"" onclick=""toggleRespectTimestamps()"">TIMESTAMPS: ON</button>
                        <span style=""color:var(--text-dim); font-size:0.75rem; margin-left:6px;"">PACE:</span>
                        <button class=""speed-btn"" id=""spd-fast"" onclick=""setCopilotSpeed(0.3)"">FAST</button>
                        <button class=""speed-btn active"" id=""spd-real"" onclick=""setCopilotSpeed(0.7)"">REALISTIC</button>
                        <button class=""speed-btn"" id=""spd-slow"" onclick=""setCopilotSpeed(1.2)"">SLOW</button>
                    </div>
                </div>

                <!-- Current Step Highlight -->
                <div id=""currentStepBanner"" class=""current-step-banner"" style=""display:none;"">
                    <div class=""title"">CURRENT ACTION REQUIRED</div>
                    <div class=""instruction"" id=""currentStepText"">--</div>
                </div>

                <div id=""checklistSteps"">
                    <p style=""color:var(--text-dim);"">No checklist steps loaded.</p>
                </div>
            </div>
        </div>

        <!-- AUTOPILOT & FULL-FLIGHT GNC TAB -->
        <div id=""tab-autopilot"" class=""tab-content"">
            <div class=""card"" style=""margin-bottom:20px; border-color:#2c4a6e;"">
                <div class=""card-header"">
                    <span>ACTIVE FLIGHT COMPUTER STATUS</span>
                    <span id=""ap-mode-badge"" class=""pill"" style=""color:var(--green);"">OFF</span>
                </div>
                <div style=""font-family:var(--font-mono); font-size:1.05rem; color:#fff; margin-bottom:12px;"" id=""ap-detail"">Autopilot disengaged — manual control.</div>
                <div class=""card-grid"" style=""grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap:10px;"">
                    <div class=""metric-row""><span class=""metric-label"">Attitude Error</span><span class=""metric-value"" id=""ap-err"">0.0 °</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Burn ΔV Rem</span><span class=""metric-value"" id=""ap-dv"">0.0 ft/s</span></div>
                    <div class=""metric-row""><span class=""metric-label"">TIG Countdown</span><span class=""metric-value"" id=""ap-tig"">0.0 s</span></div>
                    <div class=""metric-row""><span class=""metric-label"">Docking Range</span><span class=""metric-value"" id=""ap-dock"">0.00 m</span></div>
                </div>
            </div>

            <div class=""card-grid"">
                <!-- Orbital Attitude & Alignment -->
                <div class=""card"">
                    <div class=""card-header"">ATTITUDE HOLD &amp; PLATFORM ALIGNMENT</div>
                    <button class=""btn-auto"" style=""width:100%; background:#8e44ad; color:#fff; margin-bottom:12px;"" onclick=""sendAutopilotAlign()"">AUTO-ALIGN IMU / GDC / REFSMMAT</button>
                    <div class=""ap-grid"">
                        <button class=""ap-btn"" id=""apbtn-OFF"" onclick=""setApMode('OFF')"">DISENGAGE (OFF)</button>
                        <button class=""ap-btn"" id=""apbtn-KILL_ROT"" onclick=""setApMode('KILL_ROT')"">KILL ROTATION</button>
                        <button class=""ap-btn"" id=""apbtn-PROGRADE"" onclick=""setApMode('PROGRADE')"">PROGRADE</button>
                        <button class=""ap-btn"" id=""apbtn-RETROGRADE"" onclick=""setApMode('RETROGRADE')"">RETROGRADE</button>
                        <button class=""ap-btn"" id=""apbtn-NORMAL"" onclick=""setApMode('NORMAL')"">NORMAL</button>
                        <button class=""ap-btn"" id=""apbtn-ANTI_NORMAL"" onclick=""setApMode('ANTI_NORMAL')"">ANTI-NORMAL</button>
                        <button class=""ap-btn"" id=""apbtn-RADIAL_IN"" onclick=""setApMode('RADIAL_IN')"">RADIAL IN</button>
                        <button class=""ap-btn"" id=""apbtn-RADIAL_OUT"" onclick=""setApMode('RADIAL_OUT')"">RADIAL OUT</button>
                    </div>
                </div>

                <!-- Full Autonomous Flight Programs -->
                <div class=""card"">
                    <div class=""card-header"">AUTONOMOUS MISSION FLIGHT MODES</div>
                    <div style=""display:flex; flex-direction:column; gap:10px;"">
                        <button class=""ap-btn flight-mode"" id=""apbtn-AUTO_DOCK"" onclick=""setApMode('AUTO_DOCK')"">
                            AUTO-DOCK (CSM ↔ LM)<br>
                            <span style=""font-size:0.78rem; font-weight:400; color:#aac4dc;"">Aligns docking ports, nulls corridor drift &amp; closes smoothly to hard capture</span>
                        </button>
                        <button class=""ap-btn flight-mode"" id=""apbtn-AUTO_LAND"" onclick=""setApMode('AUTO_LAND')"">
                            AUTO-LAND LUNAR MODULE (PDI → TOUCHDOWN)<br>
                            <span style=""font-size:0.78rem; font-weight:400; color:#aac4dc;"">Braking burn, powered approach to target site, auto gear &amp; soft touchdown</span>
                        </button>
                        <div style=""display:flex; gap:8px; align-items:center;"">
                            <button class=""ap-btn flight-mode"" style=""flex:1;"" id=""apbtn-AUTO_ASCENT"" onclick=""startAutoAscent()"">
                                AUTO-ASCENT (LUNAR LIFTOFF → ORBIT)<br>
                                <span style=""font-size:0.78rem; font-weight:400; color:#aac4dc;"">Stages descent stage, ignites APS &amp; gravity-turns into target lunar orbit</span>
                            </button>
                            <div class=""form-field"" style=""width:90px; margin:0;"">
                                <label>TGT AP (NM)</label>
                                <input type=""number"" id=""ascentTargetNm"" value=""60"" min=""15"" max=""200"">
                            </div>
                        </div>
                        <div style=""display:flex; gap:10px;"">
                            <button class=""ap-btn flight-mode"" style=""flex:1;"" id=""apbtn-AUTO_BURN"" onclick=""setApMode('AUTO_BURN', false)"">
                                EXECUTE PLANNED BURN (AT TIG)
                            </button>
                            <button class=""ap-btn flight-mode"" style=""flex:1; background:#6e2c1b; border-color:#c0392b;"" onclick=""setApMode('AUTO_BURN', true)"">
                                IGNITE PLANNED BURN NOW
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <!-- ORBIT BURN PLANNER & LUNAR LANDING SITE TAB -->
        <div id=""tab-planner"" class=""tab-content"">
            <div class=""card-grid"">
                <!-- Custom Burn Planner -->
                <div class=""card"">
                    <div class=""card-header"">
                        <span>ORBITAL BURN PLANNER &amp; COURSE CORRECTIONS</span>
                        <span class=""pill"" id=""pln-soi"">MOON</span>
                    </div>
                    <div style=""display:flex; gap:14px; flex-wrap:wrap; font-family:var(--font-mono); font-size:0.85rem; margin-bottom:14px; color:#a9c4de;"">
                        <span>CUR AP: <strong style=""color:#fff;"" id=""pln-cur-ap"">--</strong> NM</span>
                        <span>CUR PE: <strong style=""color:#fff;"" id=""pln-cur-pe"">--</strong> NM</span>
                        <span>INC: <strong style=""color:#fff;"" id=""pln-cur-inc"">--</strong>°</span>
                        <span>T-PE: <strong style=""color:#fff;"" id=""pln-tpe"">--</strong>s</span>
                        <span>T-AP: <strong style=""color:#fff;"" id=""pln-tap"">--</strong>s</span>
                    </div>

                    <!-- Quick Smart Presets -->
                    <div style=""display:flex; gap:8px; flex-wrap:wrap; margin-bottom:14px;"">
                        <button class=""speed-btn"" onclick=""quickPlan('circ_pe', 60, 60)"">LOI / CIRC AT PE</button>
                        <button class=""speed-btn"" onclick=""quickPlan('circ_ap', 60, 60)"">CIRC AT AP</button>
                        <button class=""speed-btn"" onclick=""quickPlan('custom_orbit', 60, 60)"">60×60 NM ORBIT</button>
                        <button class=""speed-btn"" onclick=""quickPlan('doi_60x8', 60, 8.5)"">DOI 60×8.5 NM (PDI PREP)</button>
                    </div>

                    <div class=""form-grid"">
                        <div class=""form-field"">
                            <label>Plan Mode</label>
                            <select id=""plnMode"">
                                <option value=""custom_orbit"">Target Orbit (Ap × Pe)</option>
                                <option value=""circ_pe"">Circularize at Pe</option>
                                <option value=""circ_ap"">Circularize at Ap</option>
                                <option value=""doi_60x8"">DOI (Descent Orbit 8.5 NM)</option>
                                <option value=""raw"">Custom / MCC Raw ΔV</option>
                            </select>
                        </div>
                        <div class=""form-field"">
                            <label>Target Ap (NM)</label>
                            <input type=""number"" id=""plnTargetAp"" value=""60"" step=""1"">
                        </div>
                        <div class=""form-field"">
                            <label>Target Pe (NM)</label>
                            <input type=""number"" id=""plnTargetPe"" value=""60"" step=""1"">
                        </div>
                        <div class=""form-field"">
                            <label>Phase Angle (0-360°)</label>
                            <input type=""number"" id=""plnPhase"" value=""0"" step=""5"">
                        </div>
                        <div class=""form-field"">
                            <label>Raw ΔV (ft/s)</label>
                            <input type=""number"" id=""plnDv"" value=""50"" step=""5"">
                        </div>
                        <div class=""form-field"">
                            <label>Attitude (Raw)</label>
                            <select id=""plnAtt"">
                                <option value=""Prograde"">Prograde</option>
                                <option value=""Retrograde"">Retrograde</option>
                                <option value=""RadialIn"">Radial In</option>
                                <option value=""RadialOut"">Radial Out</option>
                            </select>
                        </div>
                        <div class=""form-field"">
                            <label>Engine</label>
                            <select id=""plnEngine"">
                                <option value="""">Auto Select</option>
                                <option value=""CSM_SPS"">CSM SPS</option>
                                <option value=""CSM_Translation"">CSM RCS</option>
                                <option value=""LM_DPS"">LM DPS</option>
                                <option value=""LM_Translation"">LM RCS</option>
                            </select>
                        </div>
                    </div>

                    <div style=""display:flex; gap:10px; flex-wrap:wrap; margin-bottom:14px;"">
                        <button class=""btn-auto"" style=""background:var(--blue); color:#fff;"" onclick=""computeCustomBurn()"">COMPUTE BURN</button>
                        <button class=""btn-auto"" style=""background:#27ae60; color:#fff;"" onclick=""uploadBurnToAgc()"">UPLOAD PAD TO AGC</button>
                        <button class=""btn-auto"" style=""background:#e67e22; color:#fff;"" onclick=""setApMode('AUTO_BURN', false)"">AUTO-EXECUTE AT TIG</button>
                    </div>

                    <!-- Planned Burn Solution Readout -->
                    <div style=""background:#0d141c; border:1px solid #223244; border-radius:6px; padding:12px;"">
                        <div style=""font-weight:700; color:var(--green); margin-bottom:6px;"" id=""pln-res-name"">No Burn Planned</div>
                        <div style=""font-size:0.84rem; color:var(--text-dim); margin-bottom:8px;"" id=""pln-res-status"">Select a preset or enter desired lunar orbit parameters.</div>
                        <div class=""metric-row""><span class=""metric-label"">Required ΔV / Duration</span><span class=""metric-value"" id=""pln-res-dv"">-- ft/s (-- s)</span></div>
                        <div class=""metric-row""><span class=""metric-label"">Predicted Orbit (Ap × Pe)</span><span class=""metric-value"" id=""pln-res-orb"">-- × -- NM</span></div>
                        <div class=""metric-row""><span class=""metric-label"">Engine / Attitude / TIG</span><span class=""metric-value"" id=""pln-res-meta"">--</span></div>
                        <pre class=""pad-pre"" id=""pln-res-pad"" style=""display:none;""></pre>
                    </div>
                </div>

                <!-- Interactive Lunar Landing Site Selector -->
                <div class=""card"">
                    <div class=""card-header"">
                        <span>LUNAR LANDING SITE SELECTOR</span>
                        <span style=""color:var(--green); font-family:var(--font-mono);"" id=""ls-dist"">DIST: -- NM</span>
                    </div>
                    <p style=""font-size:0.82rem; color:var(--text-dim); margin-bottom:10px;"">
                        Click anywhere on the Lunar Map below or choose a historic Apollo site to lock coordinates &amp; align the LM IMU / LPD!
                    </p>
                    <canvas id=""moonMapCanvas"" class=""moon-canvas"" width=""600"" height=""280""></canvas>

                    <div style=""display:flex; gap:6px; flex-wrap:wrap; margin:12px 0;"">
                        <button class=""speed-btn"" onclick=""pickSite('Apollo 11 — Mare Tranquillitatis', 0.67, 23.47)"">A11 Tranquility</button>
                        <button class=""speed-btn"" onclick=""pickSite('Apollo 12 — Oceanus Procellarum', -3.01, -23.42)"">A12 Procellarum</button>
                        <button class=""speed-btn"" onclick=""pickSite('Apollo 14 — Fra Mauro', -3.65, -17.47)"">A14 Fra Mauro</button>
                        <button class=""speed-btn"" onclick=""pickSite('Apollo 15 — Hadley-Apennine', 26.13, 3.63)"">A15 Hadley</button>
                        <button class=""speed-btn"" onclick=""pickSite('Apollo 16 — Descartes', -8.97, 15.50)"">A16 Descartes</button>
                        <button class=""speed-btn"" onclick=""pickSite('Apollo 17 — Taurus-Littrow', 20.19, 30.77)"">A17 Taurus-Littrow</button>
                        <button class=""speed-btn"" onclick=""pickSite('Tycho Crater', -43.31, -11.36)"">Tycho</button>
                        <button class=""speed-btn"" onclick=""pickSite('Copernicus Crater', 9.62, -20.08)"">Copernicus</button>
                    </div>

                    <div class=""form-grid"">
                        <div class=""form-field"">
                            <label>Site Name</label>
                            <input type=""text"" id=""lsName"" value=""Apollo 11 — Mare Tranquillitatis"">
                        </div>
                        <div class=""form-field"">
                            <label>Latitude (°N)</label>
                            <input type=""number"" id=""lsLat"" value=""0.67"" step=""0.1"" min=""-90"" max=""90"">
                        </div>
                        <div class=""form-field"">
                            <label>Longitude (°E)</label>
                            <input type=""number"" id=""lsLon"" value=""23.47"" step=""0.1"" min=""-180"" max=""180"">
                        </div>
                    </div>

                    <div style=""margin-bottom:12px;"">
                        <label style=""font-size:0.75rem; color:var(--text-dim); font-weight:700;"">ORBIT GROUND-TRACK SHIFT (0°–360°): <span id=""lsTrackVal"">0°</span></label>
                        <input type=""range"" id=""lsTrackSlider"" min=""0"" max=""360"" step=""0.5"" value=""0"" style=""width:100%;"" oninput=""onTrackSliderChange(this.value)"">
                    </div>

                    <div style=""display:flex; gap:10px; flex-wrap:wrap;"">
                        <button class=""btn-auto"" style=""flex:1;"" onclick=""applyLandingSite(false)"">LOCK LANDING SITE &amp; ALIGN LM IMU</button>
                        <button class=""btn-auto"" style=""background:#27ae60; color:#fff;"" onclick=""setApMode('AUTO_LAND')"">BEGIN AUTO-LAND</button>
                    </div>
                    <div style=""font-family:var(--font-mono); font-size:0.8rem; color:#8fb2d4; margin-top:8px;"" id=""ls-active-info"">Active Target: --</div>
                </div>
            </div>
        </div>

        <!-- CREW CO-OP MULTIPLAYER TAB -->
        <div id=""tab-multiplayer"" class=""tab-content"">
            <!-- Connection Status Banner -->
            <div class=""card"" style=""margin-bottom:20px; border-color:#2c4a6e;"">
                <div class=""card-header"">
                    <span>2–3 PLAYER APOLLO CREW CO-OP (FULL COCKPIT, AGC &amp; ORBIT SYNC)</span>
                    <span class=""pill"" id=""mp-mode-badge"" style=""color:#a9cce3;"">OFFLINE</span>
                </div>
                <div style=""font-family:var(--font-mono); font-size:0.95rem; color:#fff; margin-bottom:12px;"" id=""mp-status-text"">Offline — Single Player</div>
                <div style=""display:flex; gap:10px; flex-wrap:wrap;"">
                    <button class=""btn-auto"" style=""background:#27ae60; color:#fff;"" onclick=""mpTakeAuthority()"">TAKE FLIGHT CONTROL</button>
                    <button class=""btn-auto"" style=""background:var(--blue); color:#fff;"" onclick=""mpForceSync()"">FORCE FULL COCKPIT &amp; AGC SYNC</button>
                    <button class=""btn-danger"" onclick=""mpDisconnect()"">DISCONNECT</button>
                </div>
            </div>

            <div class=""card-grid"">
                <!-- Left: Crew Identity + Direct P2P / Relay Connect -->
                <div class=""card"">
                    <div class=""card-header"">CREW STATION &amp; NETWORK CONNECTION</div>
                    <div class=""form-grid"">
                        <div class=""form-field"">
                            <label>Callsign</label>
                            <input type=""text"" id=""mpCallsign"" value=""Neil"">
                        </div>
                        <div class=""form-field"">
                            <label>Crew Role</label>
                            <select id=""mpRole"">
                                <option value=""CDR"">CDR — Commander</option>
                                <option value=""CMP"">CMP — Command Module Pilot</option>
                                <option value=""LMP"">LMP — Lunar Module Pilot</option>
                                <option value=""FLIGHT"">FLIGHT — Mission Control</option>
                            </select>
                        </div>
                    </div>

                    <hr style=""border-color:#1c2532; margin:14px 0;"">
                    <div style=""font-size:0.78rem; color:var(--green); font-weight:700; margin-bottom:8px;"">OPTION A: DIRECT P2P (LAN / TAILSCALE / ZEROTIER / PORT FORWARD)</div>
                    <div class=""form-grid"">
                        <div class=""form-field"">
                            <label>Host IP (for Join)</label>
                            <input type=""text"" id=""mpHostIp"" value=""127.0.0.1"" placeholder=""e.g. 192.168.1.50 or Tailscale IP"">
                        </div>
                        <div class=""form-field"">
                            <label>TCP Port</label>
                            <input type=""number"" id=""mpPort"" value=""7777"">
                        </div>
                    </div>
                    <div style=""display:flex; gap:10px; flex-wrap:wrap; margin-bottom:14px;"">
                        <button class=""btn-auto"" style=""flex:1;"" onclick=""mpHostDirect()"">HOST MISSION (P2P)</button>
                        <button class=""btn-auto"" style=""flex:1; background:var(--blue); color:#fff;"" onclick=""mpJoinDirect()"">JOIN CREW (P2P)</button>
                    </div>

                    <hr style=""border-color:#1c2532; margin:14px 0;"">
                    <div style=""font-size:0.78rem; color:#f1c40f; font-weight:700; margin-bottom:8px;"">OPTION B: DEDICATED RELAY SERVER (ROOM CODE — NO PORT FORWARDING)</div>
                    <div class=""form-grid"">
                        <div class=""form-field"">
                            <label>Relay Server IP</label>
                            <input type=""text"" id=""mpRelayIp"" value=""127.0.0.1"">
                        </div>
                        <div class=""form-field"">
                            <label>Relay Port</label>
                            <input type=""number"" id=""mpRelayPort"" value=""7777"">
                        </div>
                        <div class=""form-field"">
                            <label>Room Code</label>
                            <input type=""text"" id=""mpRoomCode"" value=""APOLLO11"">
                        </div>
                    </div>
                    <div style=""display:flex; gap:10px; flex-wrap:wrap;"">
                        <button class=""btn-auto"" style=""flex:1; background:#8e44ad; color:#fff;"" onclick=""mpConnectRelay(true)"">HOST VIA RELAY</button>
                        <button class=""btn-auto"" style=""flex:1; background:#2980b9; color:#fff;"" onclick=""mpConnectRelay(false)"">JOIN VIA RELAY</button>
                    </div>
                </div>

                <!-- Right: Live Crew Roster & Intercom -->
                <div class=""card"">
                    <div class=""card-header"">ACTIVE CREW ROSTER</div>
                    <div id=""mpCrewList"" style=""display:flex; flex-direction:column; gap:8px; margin-bottom:16px;"">
                        <p style=""color:var(--text-dim); font-size:0.85rem;"">No crew connected yet. Host a mission or join a crewmate!</p>
                    </div>

                    <div class=""card-header"" style=""margin-top:8px;"">CREW INTERCOM &amp; SYNC FEED</div>
                    <div id=""mpIntercomLog"" style=""height:240px; overflow-y:auto; background:#0b1117; border:1px solid #1d2a38; border-radius:6px; padding:10px; font-family:var(--font-mono); font-size:0.8rem; margin-bottom:10px;"">
                        <div style=""color:var(--text-dim);"">Intercom ready. Switch flips and crew messages appear here live.</div>
                    </div>
                    <div style=""display:flex; gap:8px;"">
                        <input type=""text"" id=""mpChatInput"" placeholder=""Transmit message to crew (Press Enter)..."" style=""flex:1; padding:9px 12px; background:#161e28; border:1px solid #2b394a; border-radius:6px; color:#fff;"" onkeydown=""if(event.key==='Enter') mpSendChat()"">
                        <button class=""btn-auto"" style=""background:var(--blue); color:#fff;"" onclick=""mpSendChat()"">SEND</button>
                    </div>
                </div>
            </div>
        </div>
    </main>

    <script>
        let ws;
        let switches = {{}};
        const WS_PORT = {wsPort};

        function connectWs() {{
            const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const host = window.location.hostname || '127.0.0.1';
            ws = new WebSocket(`${{proto}}//${{host}}:${{WS_PORT}}`);

            ws.onopen = () => {{
                document.getElementById('statusDot').classList.add('connected');
                document.getElementById('statusText').innerText = 'LIVE SYNC';
                ws.send(JSON.stringify({{ action: 'get_switches' }}));
                ws.send(JSON.stringify({{ action: 'get_checklist' }}));
            }};

            ws.onclose = () => {{
                document.getElementById('statusDot').classList.remove('connected');
                document.getElementById('statusText').innerText = 'DISCONNECTED';
                setTimeout(connectWs, 1500);
            }};

            ws.onerror = (err) => {{
                console.warn('WebSocket connection error, reconnecting...', err);
            }};

            ws.onmessage = (event) => {{
                try {{
                    const msg = JSON.parse(event.data);
                    handleMessage(msg);
                }} catch (e) {{
                    console.error('Invalid JSON message', e);
                }}
            }};
        }}

        function handleMessage(msg) {{
            if (msg.type === 'telemetry') {{
                updateTelemetry(msg.data);
            }} else if (msg.type === 'switch_event') {{
                switches[msg.id] = msg.pos;
                updateSwitchRow(msg.id, msg.pos);
            }} else if (msg.type === 'all_switches') {{
                switches = msg.data || {{}};
                renderSwitches();
            }} else if (msg.type === 'checklist') {{
                renderChecklist(msg.data);
            }} else if (msg.type === 'copilot_status') {{
                updateCopilotBanner(msg.active, msg.text, msg.holding);
            }} else if (msg.type === 'director') {{
                renderDirector(msg.data);
            }} else if (msg.type === 'autopilot') {{
                renderAutopilot(msg.data);
            }} else if (msg.type === 'planner') {{
                renderPlanner(msg.data);
            }} else if (msg.type === 'multiplayer') {{
                renderMultiplayer(msg.data);
            }}
        }}

        function renderMultiplayer(mp) {{
            if (!mp) return;
            const badge = document.getElementById('mp-mode-badge');
            const hdrMp = document.getElementById('hdr-mp');
            if (badge) {{
                badge.innerText = mp.mode || 'OFFLINE';
                badge.style.color = mp.connected ? 'var(--green)' : '#a9cce3';
            }}
            document.getElementById('mp-status-text').innerText = mp.status || 'Offline';

            const crewCount = mp.crew ? mp.crew.length : 0;
            if (hdrMp) {{
                hdrMp.innerText = mp.connected ? `${{crewCount}} CREW (${{mp.hasAuthority ? 'AUTH' : 'SYNC'}})` : 'SOLO';
                hdrMp.style.color = mp.connected ? 'var(--green)' : '#a9cce3';
            }}

            const crewDiv = document.getElementById('mpCrewList');
            if (crewDiv) {{
                if (mp.crew && mp.crew.length > 0) {{
                    let html = '';
                    mp.crew.forEach(c => {{
                        const authTag = c.hasAuth ? '<span class=""step-badge"" style=""color:#f1c40f;"">FLIGHT CONTROL</span>' : '';
                        const hostTag = c.isHost ? '<span class=""step-badge"" style=""color:var(--green);"">HOST</span>' : '';
                        html += `
                            <div class=""timeline-row ${{c.hasAuth ? 'due' : ''}}"">
                                <span class=""step-badge"" style=""color:#fff; background:#2980b9;"">${{c.role}}</span>
                                <span style=""font-weight:700; flex:1;"">${{c.callsign}}</span>
                                <span class=""step-badge"">${{c.craft}}</span>
                                ${{hostTag}}
                                ${{authTag}}
                            </div>
                        `;
                    }});
                    crewDiv.innerHTML = html;
                }} else {{
                    crewDiv.innerHTML = '<p style=""color:var(--text-dim); font-size:0.85rem;"">No crew connected yet. Host a mission or join a crewmate!</p>';
                }}
            }}

            const logDiv = document.getElementById('mpIntercomLog');
            if (logDiv && mp.intercom && mp.intercom.length > 0) {{
                let html = '';
                mp.intercom.forEach(m => {{
                    const color = m.sys ? '#8fb2d4' : '#ffffff';
                    html += `<div style=""margin-bottom:4px; color:${{color}};""><span style=""color:var(--text-dim);"">[${{m.time}}]</span> <strong style=""color:var(--green);"">[${{m.role}}] ${{m.from}}:</strong> ${{m.text}}</div>`;
                }});
                logDiv.innerHTML = html;
                logDiv.scrollTop = logDiv.scrollHeight;
            }}
        }}

        function mpHostDirect() {{
            if (!ws || ws.readyState !== WebSocket.OPEN) return;
            ws.send(JSON.stringify({{
                action: 'mp_host',
                port: document.getElementById('mpPort').value || '7777',
                callsign: document.getElementById('mpCallsign').value || 'Commander',
                role: document.getElementById('mpRole').value || 'CDR'
            }}));
        }}

        function mpJoinDirect() {{
            if (!ws || ws.readyState !== WebSocket.OPEN) return;
            ws.send(JSON.stringify({{
                action: 'mp_join',
                host: document.getElementById('mpHostIp').value || '127.0.0.1',
                port: document.getElementById('mpPort').value || '7777',
                callsign: document.getElementById('mpCallsign').value || 'Pilot',
                role: document.getElementById('mpRole').value || 'CMP',
                room: '',
                asHost: 'false'
            }}));
        }}

        function mpConnectRelay(asHost) {{
            if (!ws || ws.readyState !== WebSocket.OPEN) return;
            ws.send(JSON.stringify({{
                action: 'mp_join',
                host: document.getElementById('mpRelayIp').value || '127.0.0.1',
                port: document.getElementById('mpRelayPort').value || '7777',
                callsign: document.getElementById('mpCallsign').value || 'Crew',
                role: document.getElementById('mpRole').value || (asHost ? 'CDR' : 'LMP'),
                room: document.getElementById('mpRoomCode').value || 'APOLLO11',
                asHost: String(asHost)
            }}));
        }}

        function mpDisconnect() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'mp_disconnect' }}));
            }}
        }}

        function mpSendChat() {{
            const inp = document.getElementById('mpChatInput');
            if (!inp || !inp.value.trim() || !ws || ws.readyState !== WebSocket.OPEN) return;
            ws.send(JSON.stringify({{ action: 'mp_chat', text: inp.value.trim() }}));
            inp.value = '';
        }}

        function mpTakeAuthority() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'mp_take_authority' }}));
            }}
        }}

        function mpForceSync() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'mp_force_sync' }}));
            }}
        }}

        let autoDirectorOn = false;
        let lastMoonLat = 0, lastMoonLon = 0;
        let targetSiteLat = 0.67, targetSiteLon = 23.47;

        function renderDirector(d) {{
            if (!d) return;
            autoDirectorOn = !!d.autoEnabled;
            if (d.met) document.getElementById('hdr-met').innerText = `MET ${{d.met}}`;
            if (d.phase) document.getElementById('hdr-phase').innerText = d.phase;
            if (d.soi) document.getElementById('hdr-soi').innerText = d.soi;

            document.getElementById('dir-rec-name').innerText = `Recommended: ${{d.recommendedName || 'None'}}`;
            document.getElementById('dir-rec-reason').innerText = d.recommendedReason || '';
            document.getElementById('dir-last-action').innerText = d.lastAction || '';

            const btn = document.getElementById('btnDirectorAuto');
            if (btn) {{
                btn.innerText = autoDirectorOn ? 'AUTO-DIRECTOR: ON' : 'AUTO-DIRECTOR: OFF';
                btn.style.background = autoDirectorOn ? '#27ae60' : '#2980b9';
            }}

            const listDiv = document.getElementById('dirTimelineList');
            if (listDiv && d.timeline) {{
                let html = '';
                d.timeline.forEach(item => {{
                    const cls = item.completed ? 'timeline-row done' : (item.isDue ? 'timeline-row due' : 'timeline-row');
                    const icon = item.completed ? '[OK]' : (item.isDue ? '[DUE]' : '-');
                    html += `
                        <div class=""${{cls}}"">
                            <span class=""step-badge"">${{item.time}}</span>
                            <span style=""font-weight:700;"">${{icon}} ${{item.title}}</span>
                            <span style=""color:var(--text-dim); flex:1;"">${{item.reason}}</span>
                            <span class=""step-badge"" style=""color:${{item.isDue ? '#f1c40f' : 'var(--text-dim)'}};"">${{item.phase}}</span>
                        </div>
                    `;
                }});
                listDiv.innerHTML = html || '<div style=""color:var(--text-dim); font-size:0.82rem;"">No upcoming timeline events.</div>';
            }}
        }}

        function toggleAutoDirector() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'director_toggle', enabled: String(!autoDirectorOn) }}));
            }}
        }}

        function runRecommendedChecklist() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'director_run_recommended', auto: 'true' }}));
            }}
        }}

        function resetDirectorTimeline() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'director_reset' }}));
            }}
        }}

        function renderAutopilot(ap) {{
            if (!ap) return;
            document.getElementById('ap-mode-badge').innerText = ap.mode || 'OFF';
            document.getElementById('ap-detail').innerText = ap.detail || '';
            document.getElementById('ap-err').innerText = `${{(ap.attErr || 0).toFixed(1)}} °`;
            document.getElementById('ap-dv').innerText = `${{(ap.dvRem || 0).toFixed(1)}} ft/s`;
            document.getElementById('ap-tig').innerText = `${{(ap.tigRem || 0).toFixed(1)}} s`;
            document.getElementById('ap-dock').innerText = `${{(ap.dockDist || 0).toFixed(2)}} m`;

            document.querySelectorAll('.ap-btn').forEach(b => b.classList.remove('active'));
            const activeBtn = document.getElementById(`apbtn-${{ap.mode}}`);
            if (activeBtn) activeBtn.classList.add('active');
        }}

        function setApMode(mode, immediate = false) {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'autopilot_mode', mode: mode, immediate: String(immediate) }}));
            }}
        }}

        function startAutoAscent() {{
            const tgt = document.getElementById('ascentTargetNm').value || '60';
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'autopilot_mode', mode: 'AUTO_ASCENT', targetOrbitNm: String(tgt) }}));
            }}
        }}

        function sendAutopilotAlign() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'autopilot_align' }}));
            }}
        }}

        function renderPlanner(p) {{
            if (!p) return;
            document.getElementById('pln-soi').innerText = p.soi || 'EARTH';
            document.getElementById('pln-cur-ap').innerText = (p.curAp || 0).toFixed(1);
            document.getElementById('pln-cur-pe').innerText = (p.curPe || 0).toFixed(1);
            document.getElementById('pln-cur-inc').innerText = (p.curInc || 0).toFixed(2);
            document.getElementById('pln-tpe').innerText = Math.round(p.timeToPe || 0);
            document.getElementById('pln-tap').innerText = Math.round(p.timeToAp || 0);

            lastMoonLat = p.moonLat || 0;
            lastMoonLon = p.moonLon || 0;

            if (p.plan) {{
                const pl = p.plan;
                document.getElementById('pln-res-name').innerText = pl.name || 'No Burn Planned';
                document.getElementById('pln-res-status').innerText = pl.status || '';
                document.getElementById('pln-res-dv').innerText = `${{(pl.dv || 0).toFixed(1)}} ft/s (${{(pl.burnLen || 0).toFixed(1)}} s)`;
                document.getElementById('pln-res-orb').innerText = `${{(pl.predAp || 0).toFixed(1)}} × ${{(pl.predPe || 0).toFixed(1)}} NM`;
                document.getElementById('pln-res-meta').innerText = `${{pl.engine}} | ${{pl.attitude}} | TIG T-${{(pl.tig || 0).toFixed(0)}}s`;
                const padEl = document.getElementById('pln-res-pad');
                if (pl.pad && pl.pad.trim().length > 0) {{
                    padEl.style.display = 'block';
                    padEl.innerText = pl.pad;
                }} else {{
                    padEl.style.display = 'none';
                }}
            }}

            if (p.landing) {{
                const ls = p.landing;
                document.getElementById('ls-dist').innerText = `DIST: ${{(ls.distNm || 0).toFixed(1)}} NM (PDI: ${{(ls.burnDistNm || 0).toFixed(1)}} NM)`;
                document.getElementById('ls-active-info').innerText = `Active Target: ${{ls.name}} (${{(ls.lat || 0).toFixed(2)}}°N, ${{(ls.lon || 0).toFixed(2)}}°E | Track ${{(ls.trackDeg || 0).toFixed(1)}}°)`;
            }}

            drawMoonMap();
        }}

        function quickPlan(mode, ap, pe) {{
            document.getElementById('plnMode').value = mode;
            document.getElementById('plnTargetAp').value = ap;
            document.getElementById('plnTargetPe').value = pe;
            computeCustomBurn();
        }}

        function computeCustomBurn() {{
            if (!ws || ws.readyState !== WebSocket.OPEN) return;
            ws.send(JSON.stringify({{
                action: 'plan_burn',
                mode: document.getElementById('plnMode').value,
                targetAp: document.getElementById('plnTargetAp').value,
                targetPe: document.getElementById('plnTargetPe').value,
                phase: document.getElementById('plnPhase').value,
                dv: document.getElementById('plnDv').value,
                attitude: document.getElementById('plnAtt').value,
                engine: document.getElementById('plnEngine').value
            }}));
        }}

        function uploadBurnToAgc() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'upload_burn_agc' }}));
            }}
        }}

        // Interactive Moon Landing Map
        const APOLLO_SITES = [
            {{ code: 'A11', name: 'Apollo 11 — Mare Tranquillitatis', lat: 0.67, lon: 23.47 }},
            {{ code: 'A12', name: 'Apollo 12 — Oceanus Procellarum', lat: -3.01, lon: -23.42 }},
            {{ code: 'A14', name: 'Apollo 14 — Fra Mauro', lat: -3.65, lon: -17.47 }},
            {{ code: 'A15', name: 'Apollo 15 — Hadley-Apennine', lat: 26.13, lon: 3.63 }},
            {{ code: 'A16', name: 'Apollo 16 — Descartes', lat: -8.97, lon: 15.50 }},
            {{ code: 'A17', name: 'Apollo 17 — Taurus-Littrow', lat: 20.19, lon: 30.77 }},
            {{ code: 'TYC', name: 'Tycho Crater', lat: -43.31, lon: -11.36 }},
            {{ code: 'COP', name: 'Copernicus Crater', lat: 9.62, lon: -20.08 }}
        ];

        function pickSite(name, lat, lon) {{
            document.getElementById('lsName').value = name;
            document.getElementById('lsLat').value = lat;
            document.getElementById('lsLon').value = lon;
            targetSiteLat = lat;
            targetSiteLon = lon;
            drawMoonMap();
            applyLandingSite(false);
        }}

        function onTrackSliderChange(val) {{
            document.getElementById('lsTrackVal').innerText = `${{val}}°`;
            applyLandingSite(true);
        }}

        function applyLandingSite(useTrack) {{
            const name = document.getElementById('lsName').value || 'Custom Lunar Site';
            const lat = parseFloat(document.getElementById('lsLat').value || '0');
            const lon = parseFloat(document.getElementById('lsLon').value || '0');
            const trackDeg = parseFloat(document.getElementById('lsTrackSlider').value || '0');
            targetSiteLat = lat;
            targetSiteLon = lon;
            drawMoonMap();
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{
                    action: 'select_landing_site',
                    name: name,
                    lat: String(lat),
                    lon: String(lon),
                    trackDeg: String(trackDeg),
                    useTrack: String(useTrack)
                }}));
            }}
        }}

        function drawMoonMap() {{
            const canvas = document.getElementById('moonMapCanvas');
            if (!canvas) return;
            const ctx = canvas.getContext('2d');
            const w = canvas.width, h = canvas.height;

            // Lunar surface gradient
            const grad = ctx.createRadialGradient(w * 0.5, h * 0.5, 20, w * 0.5, h * 0.5, w * 0.55);
            grad.addColorStop(0, '#2a3038');
            grad.addColorStop(1, '#12161c');
            ctx.fillStyle = grad;
            ctx.fillRect(0, 0, w, h);

            // Stylized Lunar Maria patches
            ctx.fillStyle = 'rgba(15, 20, 28, 0.55)';
            [[0.56, 0.48, 45, 32], [0.43, 0.44, 58, 46], [0.51, 0.36, 36, 26], [0.64, 0.52, 24, 24]].forEach(m => {{
                ctx.beginPath();
                ctx.ellipse(w * m[0], h * m[1], m[2], m[3], 0, 0, Math.PI * 2);
                ctx.fill();
            }});

            // Lat/Lon grid
            ctx.strokeStyle = 'rgba(130, 165, 200, 0.16)';
            ctx.lineWidth = 1;
            for (let lon = -150; lon <= 150; lon += 30) {{
                const x = ((lon + 180) / 360) * w;
                ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke();
            }}
            for (let lat = -60; lat <= 60; lat += 30) {{
                const y = ((90 - lat) / 180) * h;
                ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
            }}
            // Equator & Prime Meridian
            ctx.strokeStyle = 'rgba(130, 165, 200, 0.35)';
            ctx.beginPath(); ctx.moveTo(0, h / 2); ctx.lineTo(w, h / 2); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(w / 2, 0); ctx.lineTo(w / 2, h); ctx.stroke();

            // Historic Apollo Sites
            ctx.font = '10px Consolas, monospace';
            APOLLO_SITES.forEach(s => {{
                const x = ((s.lon + 180) / 360) * w;
                const y = ((90 - s.lat) / 180) * h;
                ctx.fillStyle = '#5dade2';
                ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
                ctx.fillStyle = '#a9cce3';
                ctx.fillText(s.code, x + 5, y + 3);
            }});

            // Selected Landing Target Crosshair
            const tx = ((targetSiteLon + 180) / 360) * w;
            const ty = ((90 - targetSiteLat) / 180) * h;
            ctx.strokeStyle = '#2ecc71';
            ctx.lineWidth = 2;
            ctx.beginPath(); ctx.arc(tx, ty, 8, 0, Math.PI * 2); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(tx - 13, ty); ctx.lineTo(tx + 13, ty); ctx.stroke();
            ctx.beginPath(); ctx.moveTo(tx, ty - 13); ctx.lineTo(tx, ty + 13); ctx.stroke();
            ctx.fillStyle = '#2ecc71';
            ctx.fillText('TARGET', tx + 10, ty - 8);

            // Live Spacecraft Sub-Lunar Position
            const sx = ((lastMoonLon + 180) / 360) * w;
            const sy = ((90 - lastMoonLat) / 180) * h;
            ctx.fillStyle = '#f1c40f';
            ctx.beginPath(); ctx.arc(sx, sy, 4.5, 0, Math.PI * 2); ctx.fill();
            ctx.fillText('CRAFT', sx + 7, sy + 4);
        }}

        document.getElementById('moonMapCanvas')?.addEventListener('click', (e) => {{
            const rect = e.target.getBoundingClientRect();
            const nx = (e.clientX - rect.left) / rect.width;
            const ny = (e.clientY - rect.top) / rect.height;
            const lon = +(nx * 360 - 180).toFixed(2);
            const lat = +(90 - ny * 180).toFixed(2);
            pickSite(`Custom (${{lat}}°, ${{lon}}°)`, lat, lon);
        }});

        let respectTimestampsOn = true;

        function updateCopilotBanner(active, text, holding) {{
            isAuto = !!active;
            const isHold = !!holding;
            const btn = document.getElementById('btnAutoToggle');
            const banner = document.getElementById('copilotBanner');
            const statusText = document.getElementById('copilotStatusText');
            const skipBtn = document.getElementById('btnSkipHold');
            if (btn) {{
                btn.innerText = isAuto ? 'STOP COPILOT' : 'AUTO-COPILOT';
                btn.style.background = isAuto ? '#e67e22' : 'var(--blue)';
            }}
            if (banner) {{
                banner.classList.toggle('running', isAuto && !isHold);
                banner.classList.toggle('holding', isAuto && isHold);
            }}
            if (skipBtn) {{
                skipBtn.style.display = (isAuto && isHold) ? 'inline-block' : 'none';
            }}
            if (statusText && text) {{
                statusText.innerText = text;
            }}
        }}

        function toggleRespectTimestamps() {{
            respectTimestampsOn = !respectTimestampsOn;
            updateTimestampButton(respectTimestampsOn);
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'checklist_timestamps', enabled: String(respectTimestampsOn) }}));
            }}
        }}

        function updateTimestampButton(enabled) {{
            if (enabled === undefined || enabled === null) return;
            respectTimestampsOn = !!enabled;
            const btn = document.getElementById('btnRespectTs');
            if (btn) {{
                btn.innerText = respectTimestampsOn ? 'TIMESTAMPS: ON' : 'TIMESTAMPS: OFF';
                btn.classList.toggle('active', respectTimestampsOn);
            }}
        }}

        function skipCopilotHold() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'checklist_skip_hold' }}));
            }}
        }}

        function updateSpeedButtons(spd) {{
            if (spd === undefined || spd === null) return;
            document.getElementById('spd-fast')?.classList.toggle('active', spd <= 0.45);
            document.getElementById('spd-real')?.classList.toggle('active', spd > 0.45 && spd < 1.0);
            document.getElementById('spd-slow')?.classList.toggle('active', spd >= 1.0);
        }}

        function updateTelemetry(data) {{
            if (!data) return;
            if (data.met) document.getElementById('hdr-met').innerText = `MET ${{data.met}}`;
            if (data.phase) document.getElementById('hdr-phase').innerText = data.phase;
            if (data.soi) document.getElementById('hdr-soi').innerText = data.soi;
            if (data.craft) document.getElementById('telem-craft').innerText = data.craft;
            if (data.ap !== undefined) document.getElementById('telem-ap').innerText = `${{data.ap.toFixed(1)}} NM`;
            if (data.pe !== undefined) document.getElementById('telem-pe').innerText = `${{data.pe.toFixed(1)}} NM`;
            if (data.vel !== undefined) document.getElementById('telem-vel').innerText = `${{data.vel.toFixed(0)}} ft/s`;
            if (data.inc !== undefined) document.getElementById('telem-inc').innerText = `${{data.inc.toFixed(2)}} °`;
            if (data.alt !== undefined) document.getElementById('telem-alt').innerText = `${{data.alt.toFixed(0)}} ft`;
            if (data.busa !== undefined) document.getElementById('telem-busa').innerText = `${{data.busa.toFixed(1)}} V`;
            if (data.busb !== undefined) document.getElementById('telem-busb').innerText = `${{data.busb.toFixed(1)}} V`;

            if (data.dsky) {{
                document.getElementById('dsky-prog').innerText = data.dsky.prog || '00';
                document.getElementById('dsky-verb').innerText = data.dsky.verb || '00';
                document.getElementById('dsky-noun').innerText = data.dsky.noun || '00';
                document.getElementById('dsky-r1').innerText = data.dsky.r1 || '+00000';
                document.getElementById('dsky-r2').innerText = data.dsky.r2 || '+00000';
                document.getElementById('dsky-r3').innerText = data.dsky.r3 || '+00000';
            }}
        }}

        function renderChecklist(data) {{
            if (!data) return;
            document.getElementById('chk-title').innerText = data.name || 'Active Checklist';
            document.getElementById('chk-subtitle').innerText = `${{data.count || 0}} steps remaining`;

            updateCopilotBanner(data.copilotActive, data.copilotActivity, data.copilotHolding);
            updateTimestampButton(data.respectTimestamps);
            updateSpeedButtons(data.copilotSpeed);

            // Populate Available Checklists Picker
            const picker = document.getElementById('checklistPicker');
            if (data.available && data.available.length > 0 && picker.options.length <= 1) {{
                picker.innerHTML = '<option value="""">-- Select a Flight Procedure / Checklist to Load --</option>';
                data.available.forEach(c => {{
                    const opt = document.createElement('option');
                    opt.value = c.path;
                    opt.innerText = `[${{c.group || 'General'}}] ${{c.name}}`;
                    picker.appendChild(opt);
                }});
            }}

            // Render current step banner
            const banner = document.getElementById('currentStepBanner');
            const currentText = document.getElementById('currentStepText');
            const stepsDiv = document.getElementById('checklistSteps');

            if (data.steps && data.steps.length > 0) {{
                const first = data.steps[0];
                banner.style.display = 'block';
                currentText.innerText = first.text + (first.target ? `  [${{first.target}}]` : '');

                let html = '';
                data.steps.forEach((s, idx) => {{
                    const cls = idx === 0 ? 'step-item current' : (s.ok ? 'step-item ok' : 'step-item');
                    const badgeColor = s.ok ? 'var(--green)' : (idx === 0 ? '#f1c40f' : 'var(--blue)');
                    const okTag = s.ok ? '<span class=""step-badge"" style=""color:var(--green);"">SET</span>' : '';
                    html += `
                        <div class=""${{cls}}"">
                            <span class=""step-badge"" style=""color:${{badgeColor}};"">#${{idx + 1}}</span>
                            <span class=""step-text"">${{s.text}}</span>
                            ${{okTag}}
                            ${{s.target ? `<span class=""step-badge"" style=""color:var(--text-dim);"">${{s.target}}</span>` : ''}}
                        </div>
                    `;
                }});
                stepsDiv.innerHTML = html;
            }} else {{
                banner.style.display = 'none';
                stepsDiv.innerHTML = '<p style=""color:var(--text-dim);"">Checklist is complete or none is running. Select a procedure above!</p>';
            }}
        }}

        function loadSelectedChecklist() {{
            const picker = document.getElementById('checklistPicker');
            if (picker.value && ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'load_checklist', path: picker.value }}));
            }}
        }}

        function sendDskyKey(key) {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'dsky_key', key: key }}));
            }}
        }}

        function sendSetPin(pinId, position) {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'set_pin', id: pinId, pos: position }}));
            }}
        }}

        function executeNextChecklistStep() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'checklist_next' }}));
            }}
        }}

        let isAuto = false;
        function toggleAutoRunner() {{
            const targetState = !isAuto;
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: targetState ? 'checklist_auto_start' : 'checklist_auto_stop' }}));
            }}
        }}

        function setCopilotSpeed(seconds) {{
            updateSpeedButtons(seconds);
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'checklist_speed', speed: String(seconds) }}));
            }}
        }}

        function clearChecklist() {{
            if (ws && ws.readyState === WebSocket.OPEN) {{
                ws.send(JSON.stringify({{ action: 'checklist_clear' }}));
            }}
        }}

        function renderSwitches() {{
            const tbody = document.getElementById('switchTableBody');
            const search = (document.getElementById('switchSearch').value || '').toLowerCase();
            const keys = Object.keys(switches).sort();
            let html = '';

            for (const key of keys) {{
                if (search && !key.toLowerCase().includes(search)) continue;
                const pos = switches[key];
                html += `
                    <tr>
                        <td style=""font-weight:700;"">${{key}}</td>
                        <td id=""sw-val-${{key}}"">${{pos}}</td>
                        <td>
                            <div class=""btn-group"">
                                <button class=""sw-btn ${{pos === 'Up' ? 'active' : ''}}"" onclick=""sendSetPin('${{key}}', 'Up')"">UP</button>
                                <button class=""sw-btn ${{pos === 'Middle' ? 'active' : ''}}"" onclick=""sendSetPin('${{key}}', 'Middle')"">MID</button>
                                <button class=""sw-btn ${{pos === 'Down' ? 'active' : ''}}"" onclick=""sendSetPin('${{key}}', 'Down')"">DOWN</button>
                            </div>
                        </td>
                    </tr>
                `;
            }}
            tbody.innerHTML = html || '<tr><td colspan=""3"">No matching switches found.</td></tr>';
        }}

        function filterSwitches() {{
            renderSwitches();
        }}

        function updateSwitchRow(id, pos) {{
            const valEl = document.getElementById(`sw-val-${{id}}`);
            if (valEl) valEl.innerText = pos;
        }}

        function switchTab(tabName, btnEl) {{
            document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
            document.querySelectorAll('.nav-btn').forEach(el => el.classList.remove('active'));
            document.getElementById(`tab-${{tabName}}`).classList.add('active');
            if (btnEl) btnEl.classList.add('active');
            if (tabName === 'planner') drawMoonMap();
        }}

        drawMoonMap();
        connectWs();
    </script>
</body>
</html>";
        }
    }
}
