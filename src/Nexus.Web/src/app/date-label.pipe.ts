import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'dateLabel',
  standalone: true
})
export class DateLabelPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }

    return date.toLocaleTimeString([], {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  }
}
