import React, { useState } from "react";
import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { LogOut } from "lucide-react";
import { useAuthContext } from "@elsa-workflows/studio-sdk";
import "./UserMenu.css";

function initials(name: string) {
  return name.split(/[\s._@-]+/).filter(Boolean).slice(0, 2).map(part => Array.from(part)[0].toUpperCase()).join("");
}

/**
 * The signed-in user's name with a Sign out action (ADR 0085 D7). Sign-out ends the backend session; the anonymous
 * session it leaves behind sends RequireAuth to the provider's challenge, the backend-owned login page. Renders
 * nothing without an authenticated session, so a deployment with sign-in off shows no menu.
 */
export function UserMenu() {
  const auth = useAuthContext();
  const [signingOut, setSigningOut] = useState(false);
  const [failed, setFailed] = useState(false);
  if (auth?.session.status !== "authenticated") return null;

  const name = auth.session.displayName || auth.session.subject || "Signed-in user";

  const signOut = async () => {
    setSigningOut(true);
    setFailed(false);
    try {
      await auth.logout();
    } catch (error) {
      console.error("Sign-out failed.", error);
      setFailed(true);
      setSigningOut(false);
    }
  };

  return (
    <div className="user-menu">
      <DropdownMenu.Root>
        <DropdownMenu.Trigger asChild>
          <button type="button" className="user-menu-button" aria-label={`Account: ${name}`} title={name}>
            <span className="user-menu-avatar" aria-hidden="true">{initials(name)}</span>
            <span className="user-menu-name">{name}</span>
          </button>
        </DropdownMenu.Trigger>
        <DropdownMenu.Portal>
          <DropdownMenu.Content className="user-menu-content" align="end" sideOffset={8}>
            <DropdownMenu.Label className="user-menu-label">
              <span className="user-menu-label-caption">Signed in as</span>
              <span className="user-menu-label-name">{name}</span>
            </DropdownMenu.Label>
            <DropdownMenu.Separator className="user-menu-separator" />
            <DropdownMenu.Item
              className="user-menu-item"
              disabled={signingOut}
              // Stay open while the request runs so a failure can be reported in place.
              onSelect={event => {
                event.preventDefault();
                void signOut();
              }}
            >
              <LogOut size={16} aria-hidden="true" />
              <span>{signingOut ? "Signing out…" : "Sign out"}</span>
            </DropdownMenu.Item>
            {failed ? <p className="user-menu-error" role="alert">Sign-out failed. Try again.</p> : null}
          </DropdownMenu.Content>
        </DropdownMenu.Portal>
      </DropdownMenu.Root>
    </div>
  );
}
