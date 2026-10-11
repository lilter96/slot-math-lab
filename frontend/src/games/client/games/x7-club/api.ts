import type { ClubResult, ClubState } from '@tgslots/x7-club'
import { RevisionedGameApi } from '../../engine/revisioned-api'
export { RevisionedApiError as ClubApiError } from '../../engine/revisioned-api'
export class ClubApi extends RevisionedGameApi<ClubState, ClubResult> {
  constructor(baseUrl = '') {
    super('x7-club', baseUrl)
  }
}
