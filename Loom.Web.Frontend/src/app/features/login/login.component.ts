import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth/auth.service';

/** Pure. The message shown for a failed login attempt, distinguishing exactly the three
 *  cases the server can return. */
export function loginErrorMessage(status: number, retryAfterHeader: string | null): string {
  if (status === 401) return 'Invalid username or password.';
  if (status === 429) {
    const seconds = retryAfterHeader !== null ? Number(retryAfterHeader) : NaN;
    return Number.isFinite(seconds)
      ? `Too many attempts. Try again in ${seconds} seconds.`
      : 'Too many attempts. Try again shortly.';
  }
  return 'Could not reach the Loom dashboard.';
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="login-page">
      <form class="login-card surface" [formGroup]="form" (ngSubmit)="submit()">
        <h1 class="logo">
          <span class="logo-icon" role="img" aria-label="Loom logo">🧵</span>
          <span>Loom</span>
        </h1>

        <label class="field">
          <span>Username</span>
          <input type="text" formControlName="username" autocomplete="username" />
        </label>

        <label class="field">
          <span>Password</span>
          <input type="password" formControlName="password" autocomplete="current-password" />
        </label>

        @if (error()) {
          <div class="error-panel" role="alert" aria-live="assertive">
            {{ error() }}
          </div>
        }

        <button type="submit" [disabled]="form.invalid || submitting()">
          {{ submitting() ? 'Signing in...' : 'Sign in' }}
        </button>
      </form>
    </div>
  `,
  styles: [`
    .login-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--bg-primary);
    }

    .login-card {
      width: 320px;
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 2rem;
      background: var(--bg-surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 20px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0 0 0.5rem;
    }

    .logo-icon {
      font-size: 24px;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      font-size: 13px;
      color: var(--text-secondary);
    }

    .field input {
      padding: 0.5rem 0.65rem;
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-primary);
      font-size: 14px;
    }

    .error-panel {
      padding: 0.5rem 0.65rem;
      border-radius: var(--radius-sm);
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      color: var(--text-secondary);
      font-size: 13px;
    }

    button[type="submit"] {
      padding: 0.6rem;
      border: none;
      border-radius: var(--radius-sm);
      background: var(--accent);
      color: var(--bg-primary);
      font-weight: 600;
      cursor: pointer;
    }

    button[type="submit"]:disabled {
      opacity: 0.6;
      cursor: default;
    }
  `]
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    username: ['', Validators.required],
    password: ['', Validators.required]
  });

  submit(): void {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.error.set(null);
    const { username, password } = this.form.getRawValue();

    this.auth.login(username, password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        void this.router.navigateByUrl(returnUrl ?? '/dashboard');
      },
      error: (error: unknown) => {
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        const retryAfter = error instanceof HttpErrorResponse ? error.headers.get('Retry-After') : null;
        this.error.set(loginErrorMessage(status, retryAfter));
        this.submitting.set(false);
      }
    });
  }
}
