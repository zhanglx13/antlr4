/*
 * [The "BSD license"]
 *  Copyright (c) 2013 Terence Parr
 *  Copyright (c) 2013 Sam Harwell
 *  All rights reserved.
 *
 *  Redistribution and use in source and binary forms, with or without
 *  modification, are permitted provided that the following conditions
 *  are met:
 *
 *  1. Redistributions of source code must retain the above copyright
 *     notice, this list of conditions and the following disclaimer.
 *  2. Redistributions in binary form must reproduce the above copyright
 *     notice, this list of conditions and the following disclaimer in the
 *     documentation and/or other materials provided with the distribution.
 *  3. The name of the author may not be used to endorse or promote products
 *     derived from this software without specific prior written permission.
 *
 *  THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
 *  IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 *  OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
 *  IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
 *  INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 *  NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 *  DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 *  THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 *  (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 *  THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Sharpen;

namespace Antlr4.Runtime.Atn
{
    /// <author>Sam Harwell</author>
    public class ATNConfigSet : ISet<ATNConfig>
    {
        /// <summary>
        /// This maps (state, alt) -&gt; merged
        /// <see cref="ATNConfig">ATNConfig</see>
        /// . The key does not account for
        /// the
        /// <see cref="ATNConfig.GetSemanticContext()">ATNConfig.GetSemanticContext()</see>
        /// of the value, which is only a problem if a single
        /// <code>ATNConfigSet</code>
        /// contains two configs with the same state and alternative
        /// but different semantic contexts. When this case arises, the first config
        /// added to this map stays, and the remaining configs are placed in
        /// <see cref="unmerged">unmerged</see>
        /// .
        /// <p>
        /// This map is only used for optimizing the process of adding configs to the set,
        /// and is
        /// <code>null</code>
        /// for read-only sets stored in the DFA.
        /// </summary>
        private readonly Dictionary<long, ATNConfig> mergedConfigs;

        /// <summary>
        /// This is an "overflow" list holding configs which cannot be merged with one
        /// of the configs in
        /// <see cref="mergedConfigs">mergedConfigs</see>
        /// but have a colliding key. This
        /// occurs when two configs in the set have the same state and alternative but
        /// different semantic contexts.
        /// <p>
        /// This list is only used for optimizing the process of adding configs to the set,
        /// and is
        /// <code>null</code>
        /// for read-only sets stored in the DFA.
        /// </summary>
        private readonly List<ATNConfig> unmerged;

        /// <summary>This is a list of all configs in this set.</summary>
        /// <remarks>This is a list of all configs in this set.</remarks>
        private readonly List<ATNConfig> configs;

        private int uniqueAlt;

        private BitArray conflictingAlts;

        private bool hasSemanticContext;

        private bool dipsIntoOuterContext;

        /// <summary>
        /// When
        /// <code>true</code>
        /// , this config set represents configurations where the entire
        /// outer context has been consumed by the ATN interpreter. This prevents the
        /// <see cref="ParserATNSimulator.Closure(ATNConfigSet, ATNConfigSet, bool, bool, PredictionContextCache)
        ///     ">ParserATNSimulator.Closure(ATNConfigSet, ATNConfigSet, bool, bool, PredictionContextCache)
        ///     </see>
        /// from pursuing the global FOLLOW when a
        /// rule stop state is reached with an empty prediction context.
        /// <p>
        /// Note:
        /// <code>outermostConfigSet</code>
        /// and
        /// <see cref="dipsIntoOuterContext">dipsIntoOuterContext</see>
        /// should never
        /// be true at the same time.
        /// </summary>
        private bool outermostConfigSet;

        public ATNConfigSet()
        {
            // Used in parser and lexer. In lexer, it indicates we hit a pred
            // while computing a closure operation.  Don't make a DFA state from this.
            this.mergedConfigs = new Dictionary<long, ATNConfig>();
            this.unmerged = new List<ATNConfig>();
            this.configs = new List<ATNConfig>();
            this.uniqueAlt = ATN.InvalidAltNumber;
        }

        protected internal ATNConfigSet(Antlr4.Runtime.Atn.ATNConfigSet set, bool @readonly
            )
        {
            if (@readonly)
            {
                this.mergedConfigs = null;
                this.unmerged = null;
            }
            else
            {
                if (!set.IsReadOnly())
                {
                    this.mergedConfigs = (Dictionary<long, ATNConfig>)set.mergedConfigs.Clone();
                    this.unmerged = (List<ATNConfig>)set.unmerged.Clone();
                }
                else
                {
                    this.mergedConfigs = new Dictionary<long, ATNConfig>(set.configs.Count);
                    this.unmerged = new List<ATNConfig>();
                }
            }
            this.configs = (List<ATNConfig>)set.configs.Clone();
            this.dipsIntoOuterContext = set.dipsIntoOuterContext;
            this.hasSemanticContext = set.hasSemanticContext;
            this.outermostConfigSet = set.outermostConfigSet;
            if (@readonly || !set.IsReadOnly())
            {
                this.uniqueAlt = set.uniqueAlt;
                this.conflictingAlts = set.conflictingAlts;
            }
        }

        // if (!readonly && set.isReadOnly()) -> addAll is called from clone()
        /// <summary>
        /// Get the set of all alternatives represented by configurations in this
        /// set.
        /// </summary>
        /// <remarks>
        /// Get the set of all alternatives represented by configurations in this
        /// set.
        /// </remarks>
        [NotNull]
        public virtual BitArray GetRepresentedAlternatives()
        {
            if (conflictingAlts != null)
            {
                return (BitArray)conflictingAlts.Clone();
            }
            BitArray alts = new BitArray();
            foreach (ATNConfig config in this)
            {
                alts.Set(config.GetAlt());
            }
            return alts;
        }

        public bool IsReadOnly()
        {
            return mergedConfigs == null;
        }

        public void StripHiddenConfigs()
        {
            EnsureWritable();
            IEnumerator<KeyValuePair<long, ATNConfig>> iterator = mergedConfigs.EntrySet().GetEnumerator
                ();
            while (iterator.HasNext())
            {
                if (iterator.Next().Value.IsHidden())
                {
                    iterator.Remove();
                }
            }
            IListIterator<ATNConfig> iterator2 = unmerged.ListIterator();
            while (iterator2.HasNext())
            {
                if (iterator2.Next().IsHidden())
                {
                    iterator2.Remove();
                }
            }
            iterator2 = configs.ListIterator();
            while (iterator2.HasNext())
            {
                if (iterator2.Next().IsHidden())
                {
                    iterator2.Remove();
                }
            }
        }

        public virtual bool IsOutermostConfigSet()
        {
            return outermostConfigSet;
        }

        public virtual void SetOutermostConfigSet(bool outermostConfigSet)
        {
            if (this.outermostConfigSet && !outermostConfigSet)
            {
                throw new InvalidOperationException();
            }
            System.Diagnostics.Debug.Assert(!outermostConfigSet || !dipsIntoOuterContext);
            this.outermostConfigSet = outermostConfigSet;
        }

        public virtual ISet<ATNState> GetStates()
        {
            ISet<ATNState> states = new HashSet<ATNState>();
            foreach (ATNConfig c in this.configs)
            {
                states.Add(c.GetState());
            }
            return states;
        }

        public virtual void OptimizeConfigs(ATNSimulator interpreter)
        {
            if (configs.IsEmpty())
            {
                return;
            }
            for (int i = 0; i < configs.Count; i++)
            {
                ATNConfig config = configs[i];
                config.SetContext(interpreter.atn.GetCachedContext(config.GetContext()));
            }
        }

        public virtual Antlr4.Runtime.Atn.ATNConfigSet Clone(bool @readonly)
        {
            Antlr4.Runtime.Atn.ATNConfigSet copy = new Antlr4.Runtime.Atn.ATNConfigSet(this, 
                @readonly);
            if (!@readonly && this.IsReadOnly())
            {
                Sharpen.Collections.AddAll(copy, this.configs);
            }
            return copy;
        }

        public virtual int Count
        {
            get
            {
                return configs.Count;
            }
        }

        public virtual bool IsEmpty()
        {
            return configs.IsEmpty();
        }

        public virtual bool Contains(object o)
        {
            if (!(o is ATNConfig))
            {
                return false;
            }
            ATNConfig config = (ATNConfig)o;
            long configKey = GetKey(config);
            ATNConfig mergedConfig = mergedConfigs.Get(configKey);
            if (mergedConfig != null && CanMerge(config, configKey, mergedConfig))
            {
                return mergedConfig.Contains(config);
            }
            foreach (ATNConfig c in unmerged)
            {
                if (c.Contains(config))
                {
                    return true;
                }
            }
            return false;
        }

        public virtual IEnumerator<ATNConfig> GetEnumerator()
        {
            return new ATNConfigSet.ATNConfigSetIterator(this);
        }

        public virtual object[] ToArray()
        {
            return Sharpen.Collections.ToArray(configs);
        }

        public virtual T[] ToArray<T>(T[] a)
        {
            return Sharpen.Collections.ToArray(configs, a);
        }

        public virtual bool AddItem(ATNConfig e)
        {
            return Add(e, null);
        }

        public virtual bool Add(ATNConfig e, PredictionContextCache contextCache)
        {
            EnsureWritable();
            System.Diagnostics.Debug.Assert(!outermostConfigSet || !e.GetReachesIntoOuterContext
                ());
            System.Diagnostics.Debug.Assert(!e.IsHidden());
            if (contextCache == null)
            {
                contextCache = PredictionContextCache.Uncached;
            }
            bool addKey;
            long key = GetKey(e);
            ATNConfig mergedConfig = mergedConfigs.Get(key);
            addKey = (mergedConfig == null);
            if (mergedConfig != null && CanMerge(e, key, mergedConfig))
            {
                mergedConfig.SetOuterContextDepth(Math.Max(mergedConfig.GetOuterContextDepth(), e
                    .GetOuterContextDepth()));
                PredictionContext joined = PredictionContext.Join(mergedConfig.GetContext(), e.GetContext
                    (), contextCache);
                UpdatePropertiesForMergedConfig(e);
                if (mergedConfig.GetContext() == joined)
                {
                    return false;
                }
                mergedConfig.SetContext(joined);
                return true;
            }
            for (int i = 0; i < unmerged.Count; i++)
            {
                ATNConfig unmergedConfig = unmerged[i];
                if (CanMerge(e, key, unmergedConfig))
                {
                    unmergedConfig.SetOuterContextDepth(Math.Max(unmergedConfig.GetOuterContextDepth(
                        ), e.GetOuterContextDepth()));
                    PredictionContext joined = PredictionContext.Join(unmergedConfig.GetContext(), e.
                        GetContext(), contextCache);
                    UpdatePropertiesForMergedConfig(e);
                    if (unmergedConfig.GetContext() == joined)
                    {
                        return false;
                    }
                    unmergedConfig.SetContext(joined);
                    if (addKey)
                    {
                        mergedConfigs[key] = unmergedConfig;
                        unmerged.Remove(i);
                    }
                    return true;
                }
            }
            configs.Add(e);
            if (addKey)
            {
                mergedConfigs[key] = e;
            }
            else
            {
                unmerged.Add(e);
            }
            UpdatePropertiesForAddedConfig(e);
            return true;
        }

        private void UpdatePropertiesForMergedConfig(ATNConfig config)
        {
            // merged configs can't change the alt or semantic context
            dipsIntoOuterContext |= config.GetReachesIntoOuterContext();
            System.Diagnostics.Debug.Assert(!outermostConfigSet || !dipsIntoOuterContext);
        }

        private void UpdatePropertiesForAddedConfig(ATNConfig config)
        {
            if (configs.Count == 1)
            {
                uniqueAlt = config.GetAlt();
            }
            else
            {
                if (uniqueAlt != config.GetAlt())
                {
                    uniqueAlt = ATN.InvalidAltNumber;
                }
            }
            hasSemanticContext |= !SemanticContext.None.Equals(config.GetSemanticContext());
            dipsIntoOuterContext |= config.GetReachesIntoOuterContext();
            System.Diagnostics.Debug.Assert(!outermostConfigSet || !dipsIntoOuterContext);
        }

        protected internal virtual bool CanMerge(ATNConfig left, long leftKey, ATNConfig 
            right)
        {
            if (left.GetState().stateNumber != right.GetState().stateNumber)
            {
                return false;
            }
            if (leftKey != GetKey(right))
            {
                return false;
            }
            return left.GetSemanticContext().Equals(right.GetSemanticContext());
        }

        protected internal virtual long GetKey(ATNConfig e)
        {
            long key = e.GetState().stateNumber;
            key = (key << 12) | (e.GetAlt() & unchecked((int)(0xFFF)));
            return key;
        }

        public virtual bool Remove(object o)
        {
            EnsureWritable();
            throw new NotSupportedException("Not supported yet.");
        }

        public virtual bool ContainsAll<_T0>(ICollection<_T0> c)
        {
            foreach (object o in c)
            {
                if (!(o is ATNConfig))
                {
                    return false;
                }
                if (!Contains((ATNConfig)o))
                {
                    return false;
                }
            }
            return true;
        }

        public virtual bool AddAll<_T0>(ICollection<_T0> c) where _T0:ATNConfig
        {
            return AddAll(c, null);
        }

        public virtual bool AddAll<_T0>(ICollection<_T0> c, PredictionContextCache contextCache
            ) where _T0:ATNConfig
        {
            EnsureWritable();
            bool changed = false;
            foreach (ATNConfig group in c)
            {
                changed |= Add(group, contextCache);
            }
            return changed;
        }

        public virtual bool RetainAll<_T0>(ICollection<_T0> c)
        {
            EnsureWritable();
            throw new NotSupportedException("Not supported yet.");
        }

        public virtual bool RemoveAll<_T0>(ICollection<_T0> c)
        {
            EnsureWritable();
            throw new NotSupportedException("Not supported yet.");
        }

        public virtual void Clear()
        {
            EnsureWritable();
            mergedConfigs.Clear();
            unmerged.Clear();
            configs.Clear();
            dipsIntoOuterContext = false;
            hasSemanticContext = false;
            uniqueAlt = ATN.InvalidAltNumber;
            conflictingAlts = null;
        }

        public override bool Equals(object obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (!(obj is Antlr4.Runtime.Atn.ATNConfigSet))
            {
                return false;
            }
            Antlr4.Runtime.Atn.ATNConfigSet other = (Antlr4.Runtime.Atn.ATNConfigSet)obj;
            return this.outermostConfigSet == other.outermostConfigSet && Utils.Equals(conflictingAlts
                , other.conflictingAlts) && configs.Equals(other.configs);
        }

        public override int GetHashCode()
        {
            int hashCode = 1;
            hashCode = 5 * hashCode ^ (outermostConfigSet ? 1 : 0);
            hashCode = 5 * hashCode ^ configs.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            return ToString(false);
        }

        public virtual string ToString(bool showContext)
        {
            StringBuilder buf = new StringBuilder();
            IList<ATNConfig> sortedConfigs = new List<ATNConfig>(configs);
            sortedConfigs.Sort(new _IComparer_467());
            buf.Append("[");
            for (int i = 0; i < sortedConfigs.Count; i++)
            {
                if (i > 0)
                {
                    buf.Append(", ");
                }
                buf.Append(sortedConfigs[i].ToString(null, true, showContext));
            }
            buf.Append("]");
            if (hasSemanticContext)
            {
                buf.Append(",hasSemanticContext=").Append(hasSemanticContext);
            }
            if (uniqueAlt != ATN.InvalidAltNumber)
            {
                buf.Append(",uniqueAlt=").Append(uniqueAlt);
            }
            if (conflictingAlts != null)
            {
                buf.Append(",conflictingAlts=").Append(conflictingAlts);
            }
            if (dipsIntoOuterContext)
            {
                buf.Append(",dipsIntoOuterContext");
            }
            return buf.ToString();
        }

        private sealed class _IComparer_467 : IComparer<ATNConfig>
        {
            public _IComparer_467()
            {
            }

            public int Compare(ATNConfig o1, ATNConfig o2)
            {
                if (o1.GetAlt() != o2.GetAlt())
                {
                    return o1.GetAlt() - o2.GetAlt();
                }
                else
                {
                    if (o1.GetState().stateNumber != o2.GetState().stateNumber)
                    {
                        return o1.GetState().stateNumber - o2.GetState().stateNumber;
                    }
                    else
                    {
                        return Sharpen.Runtime.CompareOrdinal(o1.GetSemanticContext().ToString(), o2.GetSemanticContext
                            ().ToString());
                    }
                }
            }
        }

        public virtual int GetUniqueAlt()
        {
            return uniqueAlt;
        }

        public virtual bool HasSemanticContext()
        {
            return hasSemanticContext;
        }

        public virtual void ClearExplicitSemanticContext()
        {
            EnsureWritable();
            hasSemanticContext = false;
        }

        public virtual void MarkExplicitSemanticContext()
        {
            EnsureWritable();
            hasSemanticContext = true;
        }

        public virtual BitArray GetConflictingAlts()
        {
            return conflictingAlts;
        }

        public virtual void SetConflictingAlts(BitArray conflictingAlts)
        {
            EnsureWritable();
            this.conflictingAlts = conflictingAlts;
        }

        public virtual bool GetDipsIntoOuterContext()
        {
            return dipsIntoOuterContext;
        }

        public virtual ATNConfig Get(int index)
        {
            return configs[index];
        }

        public virtual void Remove(int index)
        {
            EnsureWritable();
            ATNConfig config = configs[index];
            configs.Remove(config);
            long key = GetKey(config);
            if (mergedConfigs.Get(key) == config)
            {
                Sharpen.Collections.Remove(mergedConfigs, key);
            }
            else
            {
                for (int i = 0; i < unmerged.Count; i++)
                {
                    if (unmerged[i] == config)
                    {
                        unmerged.Remove(i);
                        return;
                    }
                }
            }
        }

        protected internal void EnsureWritable()
        {
            if (IsReadOnly())
            {
                throw new InvalidOperationException("This ATNConfigSet is read only.");
            }
        }

        private sealed class ATNConfigSetIterator : IEnumerator<ATNConfig>
        {
            internal int index = -1;

            internal bool removed = false;

            public override bool HasNext()
            {
                return this.index + 1 < this._enclosing.configs.Count;
            }

            public override ATNConfig Next()
            {
                if (!this.HasNext())
                {
                    throw new NoSuchElementException();
                }
                this.index++;
                this.removed = false;
                return this._enclosing.configs[this.index];
            }

            public override void Remove()
            {
                if (this.removed || this.index < 0 || this.index >= this._enclosing.configs.Count)
                {
                    throw new InvalidOperationException();
                }
                this._enclosing.Remove(this.index);
                this.removed = true;
            }

            internal ATNConfigSetIterator(ATNConfigSet _enclosing)
            {
                this._enclosing = _enclosing;
            }

            private readonly ATNConfigSet _enclosing;
        }
    }
}
