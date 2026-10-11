import { StudioHttpError } from "../../sdk";

export function getAgentErrorMessage(error: unknown) {
  if (error instanceof StudioHttpError) {
    return error.message;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return String(error);
}
