/// <reference types="vite/client" />
import {
  ConfirmHoldResponse,
  CreateClassRequest,
  CreateClassResponse,
  CreateHoldResponse,
  HoldDetailResponse,
  MyHoldItem,
  SchoolClassSummary,
} from './types';

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:8080').replace(/\/$/, '');

async function request<T>(path: string, options: RequestInit = {}, studentId?: string): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string>),
  };

  if (studentId) {
    headers['X-Student-Id'] = studentId.trim();
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...options,
      headers,
    });
  } catch {
    throw new Error('সার্ভারের সাথে সংযোগ স্থাপন করা যাচ্ছে না। অনুগ্রহ করে সার্ভার চালু আছে কিনা দেখুন।');
  }

  if (response.status === 204) {
    return undefined as T;
  }

  let data: any = null;
  try {
    data = await response.json();
  } catch {
    data = null;
  }

  if (!response.ok) {
    const serverMessage = data?.message || data?.title;
    if (serverMessage) {
      throw new Error(serverMessage);
    }
    if (response.status === 409) {
      throw new Error('আসন খালি নেই অথবা এই ক্লাসে আপনার ইতিমধ্যে একটি আবেদন রয়েছে।');
    }
    if (response.status === 400) {
      throw new Error('ভুল তথ্য প্রদান করা হয়েছে। অনুগ্রহ করে ইনপুট যাচাই করুন।');
    }
    throw new Error(`ত্রুটি ঘটেছে (HTTP ${response.status})।`);
  }

  return data as T;
}

export const api = {
  getClasses: () => request<SchoolClassSummary[]>('/classes'),

  getClassById: (classId: string) => request<SchoolClassSummary>(`/classes/${classId}`),

  createClass: (payload: CreateClassRequest) =>
    request<CreateClassResponse>('/classes', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  createHold: (classId: string, studentId: string) =>
    request<CreateHoldResponse>(
      `/classes/${classId}/holds`,
      { method: 'POST' },
      studentId
    ),

  getHold: (holdId: string) => request<HoldDetailResponse>(`/holds/${holdId}`),

  confirmHold: (holdId: string, studentId: string) =>
    request<ConfirmHoldResponse>(
      `/holds/${holdId}/confirm`,
      { method: 'POST' },
      studentId
    ),

  cancelHold: (holdId: string, studentId: string) =>
    request<void>(
      `/holds/${holdId}`,
      { method: 'DELETE' },
      studentId
    ),

  getMyHolds: (studentId: string) =>
    request<MyHoldItem[]>('/me/holds', { method: 'GET' }, studentId),
};