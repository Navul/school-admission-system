import type { Hold, SchoolClass } from "./types";

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const response = await fetch(url, { headers: { "Content-Type": "application/json" }, ...options });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.message ?? "The request failed.");
  }
  return response.status === 204 ? (undefined as T) : response.json();
}

export const getClasses = () => request<SchoolClass[]>("/api/classes");
export const createHold = (classId: string, studentName: string) =>
  request<Hold>(`/api/classes/${classId}/holds`, { method: "POST", body: JSON.stringify({ studentName }) });
export const releaseHold = (holdId: string) => request<void>(`/api/holds/${holdId}`, { method: "DELETE" });

