from pathlib import Path
import json,matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
r=Path('/private/tmp/elsa-runtime-efficiency-program');rows=json.loads((r/'m5-diagnostics-windows.json').read_text());out=Path('docs/reports/evidence/runtime-efficiency-program-2026-11-01');out.mkdir(parents=True,exist_ok=True)
fig,axes=plt.subplots(1,2,figsize=(11,4.2),sharey=True,layout='constrained');colors=['#2563eb','#d97706','#059669']
for ax,mode in zip(axes,['Durable','Balanced']):
 for (diag,placement,label),color in zip([('Off','same-device','Diagnostics persistence Off'),('On','same-device','On: runtime OS volume'),('On','separate-device','On: separate temp volume')],colors):
  selected=[x for x in rows if x['mode']==mode and x['diagnostics']==diag and x['placement']==placement];ax.plot([x['clients'] for x in selected],[x['rate'] for x in selected],marker='o',label=label,color=color,linewidth=2)
 ax.set_title(mode);ax.set_xscale('log',base=2);ax.set_xticks([1,2,4,8,16,32],[1,2,4,8,16,32]);ax.set_xlabel('Concurrent closed-loop callers');ax.set_ylim(0, max(r['rate'] for r in rows)*1.1);ax.grid(alpha=.18)
axes[0].set_ylabel('Successful HTTP workflows / second');axes[1].legend(fontsize=8,loc='upper left');fig.suptitle('HTTP4: diagnostics storage placement changes measured capacity',fontsize=13)
fig.text(.5,-.07,'32 valid 60-second windows; 15-second warmups. One ramp per configuration; no error bars or SLO.\nPostgreSQL 16 + SQLite diagnostics, D8ds_v5 (8 vCPU). Separate placement also changes device speed. Library 63e4c1fc4.',ha='center',fontsize=8)
fig.savefig(out/'diagnostics-throughput.png',dpi=180,bbox_inches='tight');fig.savefig(out/'diagnostics-throughput.svg',bbox_inches='tight');print(out/'diagnostics-throughput.png')
