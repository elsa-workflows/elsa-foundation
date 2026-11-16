import json,pathlib,matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
p=pathlib.Path(__file__).resolve().parent
data=json.loads((p/'summary.json').read_text())
modes=['Durable','Balanced','Ephemeral','EphemeralNoRecord']
labels=['Durable','Balanced · cap 50','Ephemeral','Ephemeral · no completion record']
colors=['#64748b','#2563eb','#0f766e','#b45309']
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':10,'axes.spines.top':False,'axes.spines.right':False,'axes.titleweight':'bold','axes.labelcolor':'#334155','text.color':'#0f172a'})
fig,axes=plt.subplots(1,2,figsize=(13,5.7),layout='constrained')
for mode,label,color in zip(modes,labels,colors):
 rs=sorted([r for r in data if r['phase']=='baseline' and r['mode']==mode],key=lambda r:r['leaves'])
 x=np.arange(len(rs))
 for ax,field,factor in [(axes[0],'WorkflowsPerSecond',1),(axes[1],'AllocatedBytesPerWorkflow',1/1048576)]:
  y=[r[field]*factor for r in rs]
  ax.plot(x,y,'o-',label=label,color=color,lw=2,markersize=6)
  if field=='WorkflowsPerSecond':
   ax.errorbar(x,y,yerr=[[r[field]-r['throughputMin'] for r in rs],[r['throughputMax']-r[field] for r in rs]],fmt='none',color=color,capsize=3,alpha=.65)
  ax.set_xticks([0,1,2],['1','10','100']);ax.set_xlabel('Leaf activities per workflow');ax.set_yscale('log');ax.grid(axis='y',alpha=.15)
axes[0].set(title='Completed workflows per second',ylabel='Workflows / second · log scale')
axes[1].set(title='Managed allocation per workflow',ylabel='MiB / workflow · log scale')
fig.suptitle('Elsa 4 native runtime · PostgreSQL · one caller',fontsize=17,fontweight='bold')
fig.legend(*axes[0].get_legend_handles_labels(),loc='outside lower center',ncol=2,frameon=False)
fig.savefig(p/'native-performance.png',dpi=180,bbox_inches='tight');fig.savefig(p/'native-performance.svg',bbox_inches='tight')
fig,ax=plt.subplots(figsize=(9,5.3),layout='constrained')
for mode,label,color in zip(modes,labels,colors):
 rs=[r for r in data if (r['phase'] in ['scaling','capacity'] and r['leaves']==10 and not r['external'] and r['mode']==mode) or (r['phase']=='baseline' and r['leaves']==10 and r['mode']==mode)]
 if len(rs)<2:continue
 rs.sort(key=lambda r:r['concurrency'])
 ax.plot([r['concurrency'] for r in rs],[r['WorkflowsPerSecond'] for r in rs],'o-',label=label,color=color,lw=2)
 ax.errorbar([r['concurrency'] for r in rs],[r['WorkflowsPerSecond'] for r in rs],yerr=[[r['WorkflowsPerSecond']-r['throughputMin'] for r in rs],[r['throughputMax']-r['WorkflowsPerSecond'] for r in rs]],fmt='none',color=color,capsize=3,alpha=.65)
ax.set(title='One process · ten leaf activities · PostgreSQL',xlabel='Concurrent callers',ylabel='Completed workflows / second',xticks=[1,4,16,64]);ax.grid(alpha=.15);ax.legend(frameon=False)
fig.savefig(p/'concurrency.png',dpi=180,bbox_inches='tight');fig.savefig(p/'concurrency.svg',bbox_inches='tight')
