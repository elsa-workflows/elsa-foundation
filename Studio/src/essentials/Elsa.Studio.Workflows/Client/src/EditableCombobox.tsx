import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { ChevronDown } from "lucide-react";
import "./EditableCombobox.css";

/** Suggestions assist entry without restricting the value to a predefined choice. */
export function EditableCombobox({ label, value, suggestions, onChange, disabled = false, name, placeholder, spellCheck, className = "" }: {
  label: string;
  value: string;
  suggestions: readonly string[];
  onChange(value: string): void;
  disabled?: boolean;
  name?: string;
  placeholder?: string;
  spellCheck?: boolean;
  className?: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const inputId = useId();
  const listboxId = `${inputId}-suggestions`;
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState("");
  const [activeIndex, setActiveIndex] = useState(-1);
  useEffect(() => {
    if (disabled) {
      setOpen(false);
      setActiveIndex(-1);
    }
  }, [disabled]);
  const choices = suggestions.filter(suggestion => suggestion.toLocaleLowerCase().includes(filter.trim().toLocaleLowerCase()));
  const expanded = open && !disabled && choices.length > 0;
  const activeSuggestion = expanded ? choices[activeIndex] : undefined;
  const choose = (suggestion: string) => {
    onChange(suggestion);
    setOpen(false);
    setActiveIndex(-1);
  };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (choices.length && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
      event.preventDefault();
      setOpen(true);
      setActiveIndex(current => event.key === "ArrowDown"
        ? Math.min(Math.max(current + 1, 0), choices.length - 1)
        : current <= 0 ? choices.length - 1 : current - 1);
    } else if (event.key === "Enter" && expanded && activeSuggestion) {
      event.preventDefault();
      choose(activeSuggestion);
    } else if (event.key === "Escape" && expanded) {
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
      setActiveIndex(-1);
    }
  };

  return <div className={`wf-editable-field ${className}`}><label htmlFor={inputId}>{label}</label><div className="wf-editable-combobox">
    <input
      id={inputId}
      ref={inputRef}
      name={name}
      role="combobox"
      aria-label={label}
      aria-autocomplete="list"
      aria-controls={expanded ? listboxId : undefined}
      aria-haspopup="listbox"
      aria-expanded={expanded}
      aria-activedescendant={activeSuggestion ? `${listboxId}-${activeIndex}` : undefined}
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      spellCheck={spellCheck}
      autoComplete="off"
      onChange={event => { onChange(event.target.value); setFilter(event.target.value); setOpen(true); setActiveIndex(-1); }}
      onFocus={() => { setFilter(""); setOpen(true); setActiveIndex(-1); }}
      onBlur={() => { setOpen(false); setActiveIndex(-1); }}
      onKeyDown={onKeyDown}
    />
    <button
      type="button"
      className="wf-combobox-toggle"
      aria-label={`Show ${label.toLocaleLowerCase()} suggestions`}
      aria-expanded={expanded}
      aria-controls={expanded ? listboxId : undefined}
      tabIndex={-1}
      disabled={disabled}
      onPointerDown={event => event.preventDefault()}
      onClick={() => {
        const wasExpanded = expanded;
        inputRef.current?.focus();
        setFilter("");
        setOpen(!wasExpanded);
        setActiveIndex(-1);
      }}
    ><ChevronDown size={16} aria-hidden /></button>
    {expanded ? <ul id={listboxId} role="listbox" aria-label={`${label} suggestions`}>
      {choices.map((suggestion, index) => <li key={suggestion} role="none">
        <button id={`${listboxId}-${index}`} role="option" aria-selected={index === activeIndex} tabIndex={-1} type="button" onPointerDown={event => event.preventDefault()} onClick={() => choose(suggestion)}>{suggestion}</button>
      </li>)}
    </ul> : null}
  </div></div>;
}
